using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Alba.Plist
{
    public class Plist
    {
        private const long MagicHeader = 0x30307473696c7062;

        private readonly List<int> _offsetTable = new List<int>();
        private List<byte> _objectTable = new List<byte>();
        private int _refCount;
        private int _objRefSize;
        private int _offsetByteSize;
        private long _offsetTableOffset;

        private Plist ()
        {}

        public static PlistType GetPlistType (Stream stream)
        {
            var magicHeader = new byte[8];
            stream.Read(magicHeader, 0, 8);
            PlistType plistType = BitConverter.ToInt64(magicHeader, 0) == MagicHeader ? PlistType.Binary : PlistType.Xml;
            stream.Seek(0, SeekOrigin.Begin);
            return plistType;
        }

        public static object ReadFile (string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                return ReadStream(stream);
        }

        public static object ReadString (string source)
        {
            return ReadBytes(Encoding.UTF8.GetBytes(source));
        }

        public static object ReadBytes (byte[] data)
        {
            return ReadStream(new MemoryStream(data));
        }

        public static object ReadStream (Stream stream, PlistType type = PlistType.Auto)
        {
            if (type == PlistType.Auto)
                type = GetPlistType(stream);

            switch (type) {
                case PlistType.Binary:
                    using (var reader = new BinaryReader(stream))
                        return new Plist().ReadBinary(reader.ReadBytes((int)reader.BaseStream.Length));
                case PlistType.Xml:
                    var xml = new XmlDocument { XmlResolver = null };
                    xml.Load(stream);
                    return new Plist().ReadXml(xml);
            }
            throw new ArgumentOutOfRangeException("type");
        }

        public static void WriteXmlFile (object value, string path)
        {
            using (var writer = new StreamWriter(path))
                writer.Write(ToXmlString(value));
        }

        public static void WriteXmlFile (object value, Stream stream)
        {
            using (var writer = new StreamWriter(stream))
                writer.Write(ToXmlString(value));
        }

        public static void WriteXmlWriter (object value, XmlWriter writer)
        {
            writer.WriteStartDocument();
            writer.WriteDocType("plist", "-//Apple Computer//DTD PLIST 1.0//EN", "http://www.apple.com/DTDs/PropertyList-1.0.dtd", null);
            writer.WriteStartElement("plist");
            writer.WriteAttributeString("version", "1.0");
            new Plist().Compose(value, writer);
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        public static string ToXmlString (object value)
        {
            using (var ms = new MemoryStream()) {
                var xmlWriterSettings = new XmlWriterSettings {
                    Encoding = new UTF8Encoding(false),
                    ConformanceLevel = ConformanceLevel.Document,
                    Indent = true,
                };
                using (XmlWriter xmlWriter = XmlWriter.Create(ms, xmlWriterSettings))
                    WriteXmlWriter(value, xmlWriter);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        public static XmlDocument ToXmlDocument (object value)
        {
            var doc = new XmlDocument();
            using (XmlWriter xmlWriter = doc.CreateNavigator().AppendChild())
                WriteXmlWriter(value, xmlWriter);
            return doc;
        }

        public static XDocument ToXDocument (object value)
        {
            var doc = new XDocument();
            using (XmlWriter xmlWriter = doc.CreateWriter())
                WriteXmlWriter(value, xmlWriter);
            return doc;
        }

        public static void WriteBinaryFile (object value, string path)
        {
            WriteBinaryFile(value, File.Create(path));
        }

        public static void WriteBinaryFile (object value, Stream stream)
        {
            using (var writer = new BinaryWriter(stream))
                writer.Write(ToBytes(value));
        }

        public static byte[] ToBytes (object value)
        {
            return new Plist().WriteBinary(value);
        }

        private byte[] WriteBinary (object value)
        {
            //Do not count the root node, subtract by 1
            int totalRefs = CountObject(value) - 1;
            _refCount = totalRefs;
            _objRefSize = RegulateNullBytes(BitConverter.GetBytes(_refCount)).Length;

            ComposeBinary(value);
            WriteBinaryString("bplist00", false);

            _offsetTableOffset = _objectTable.Count;
            _offsetTable.Add(_objectTable.Count - 8);
            _offsetByteSize = RegulateNullBytes(BitConverter.GetBytes(_offsetTable[_offsetTable.Count - 1])).Length;
            var offsetBytes = new List<byte>();
            _offsetTable.Reverse();

            for (int i = 0; i < _offsetTable.Count; i++) {
                _offsetTable[i] = _objectTable.Count - _offsetTable[i];
                byte[] buffer = RegulateNullBytes(BitConverter.GetBytes(_offsetTable[i]), _offsetByteSize);
                Array.Reverse(buffer);
                offsetBytes.AddRange(buffer);
            }

            _objectTable.AddRange(offsetBytes);
            _objectTable.AddRange(new byte[6]);
            _objectTable.Add(Convert.ToByte(_offsetByteSize));
            _objectTable.Add(Convert.ToByte(_objRefSize));

            var a = BitConverter.GetBytes((long)totalRefs + 1);
            Array.Reverse(a);
            _objectTable.AddRange(a);

            _objectTable.AddRange(BitConverter.GetBytes((long)0));
            a = BitConverter.GetBytes(_offsetTableOffset);
            Array.Reverse(a);
            _objectTable.AddRange(a);

            return _objectTable.ToArray();
        }

        private object ReadXml (XmlDocument xml)
        {
            XmlNode rootNode = xml.DocumentElement.ChildNodes[0];
            return Parse(rootNode);
        }

        private object ReadBinary (byte[] data)
        {
            var bList = new List<byte>(data);
            List<byte> trailer = bList.GetRange(bList.Count - 32, 32);
            ParseTrailer(trailer);
            _objectTable = bList.GetRange(0, (int)_offsetTableOffset);
            List<byte> offsetTableBytes = bList.GetRange((int)_offsetTableOffset, bList.Count - (int)_offsetTableOffset - 32);
            ParseOffsetTable(offsetTableBytes);
            return ParseBinary(0);
        }

        private Dictionary<string, object> ParseDictionary (XmlNode node)
        {
            XmlNodeList children = node.ChildNodes;
            if (children.Count % 2 != 0)
                throw new PlistFormatException("Dictionary elements must have an even number of child nodes.");

            var dict = new Dictionary<string, object>();
            for (int i = 0; i < children.Count; i += 2) {
                XmlNode keynode = children[i];
                XmlNode valnode = children[i + 1];

                if (keynode.Name != "key")
                    throw new PlistFormatException("Expected a key node.");

                object result = Parse(valnode);
                if (result != null)
                    dict.Add(keynode.InnerText, result);
            }

            return dict;
        }

        private List<object> ParseArray (XmlNode node)
        {
            return node.ChildNodes.Cast<XmlNode>().Select(Parse).Where(o => o != null).ToList();
        }

        private void ComposeArray (List<object> value, XmlWriter writer)
        {
            writer.WriteStartElement("array");
            foreach (object obj in value)
                Compose(obj, writer);
            writer.WriteEndElement();
        }

        private object Parse (XmlNode node)
        {
            switch (node.Name) {
                case "dict":
                    return ParseDictionary(node);
                case "array":
                    return ParseArray(node);
                case "string":
                    return node.InnerText;
                case "integer":
                    return Convert.ToInt32(node.InnerText, System.Globalization.NumberFormatInfo.InvariantInfo);
                case "real":
                    return Convert.ToDouble(node.InnerText, System.Globalization.NumberFormatInfo.InvariantInfo);
                case "false":
                    return false;
                case "true":
                    return true;
                case "null":
                    return null;
                case "date":
                    return XmlConvert.ToDateTime(node.InnerText, XmlDateTimeSerializationMode.Utc);
                case "data":
                    return Convert.FromBase64String(node.InnerText);
            }

            throw new PlistFormatException(String.Format("Plist node of type '{0}' is not supported.", node.Name));
        }

        private void Compose (object value, XmlWriter writer)
        {
            if (value == null || value is string) {
                writer.WriteElementString("string", value as string);
                return;
            }
            if (value is int || value is long) {
                writer.WriteElementString("integer", ((int)value).ToString(System.Globalization.NumberFormatInfo.InvariantInfo));
                return;
            }
            if (value is Dictionary<string, object> ||
                value.GetType().ToString().StartsWith("System.Collections.Generic.Dictionary`2[System.String")) {
                //Convert to Dictionary<string, object>
                var dic = value as Dictionary<string, object>;
                if (dic == null) {
                    var idic = (IDictionary)value;
                    dic = idic.Keys.Cast<object>().ToDictionary(key => key.ToString(), key => idic[key]);
                }
                WriteDictionaryValues(dic, writer);
                return;
            }
            var list = value as List<object>;
            if (list != null) {
                ComposeArray(list, writer);
                return;
            }
            var bytes = value as byte[];
            if (bytes != null) {
                writer.WriteElementString("data", Convert.ToBase64String(bytes));
                return;
            }
            if (value is float || value is double) {
                writer.WriteElementString("real", ((double)value).ToString(System.Globalization.NumberFormatInfo.InvariantInfo));
                return;
            }
            if (value is DateTime) {
                var time = (DateTime)value;
                writer.WriteElementString("date", XmlConvert.ToString(time, XmlDateTimeSerializationMode.Utc));
                return;
            }
            if (value is bool) {
                writer.WriteElementString(value.ToString().ToLower(), "");
                return;
            }
            throw new ArgumentException(String.Format("Value of type '{0}' cannot be handled.", value.GetType()), "value");
        }

        private void WriteDictionaryValues (Dictionary<string, object> dictionary, XmlWriter writer)
        {
            writer.WriteStartElement("dict");
            foreach (string key in dictionary.Keys) {
                object value = dictionary[key];
                writer.WriteElementString("key", key);
                Compose(value, writer);
            }
            writer.WriteEndElement();
        }

        private int CountObject (object value)
        {
            int count = 0;
            switch (value.GetType().ToString()) {
                case "System.Collections.Generic.Dictionary`2[System.String,System.Object]":
                    var dict = (Dictionary<string, object>)value;
                    count += dict.Keys.Sum(key => CountObject(dict[key]));
                    count += dict.Keys.Count;
                    count++;
                    break;
                case "System.Collections.Generic.List`1[System.Object]":
                    var list = (List<object>)value;
                    count += list.Sum(obj => CountObject(obj));
                    count++;
                    break;
                default:
                    count++;
                    break;
            }
            return count;
        }

        private void WriteBinaryDictionary (Dictionary<string, object> dictionary)
        {
            var buffer = new List<byte>();
            var header = new List<byte>();
            var refs = new List<int>();
            for (int i = dictionary.Count - 1; i >= 0; i--) {
                var o = new object[dictionary.Count];
                dictionary.Values.CopyTo(o, 0);
                ComposeBinary(o[i]);
                _offsetTable.Add(_objectTable.Count);
                refs.Add(_refCount);
                _refCount--;
            }
            for (int i = dictionary.Count - 1; i >= 0; i--) {
                var o = new string[dictionary.Count];
                dictionary.Keys.CopyTo(o, 0);
                ComposeBinary(o[i]); //);
                _offsetTable.Add(_objectTable.Count);
                refs.Add(_refCount);
                _refCount--;
            }

            if (dictionary.Count < 15) {
                header.Add(Convert.ToByte(0xD0 | Convert.ToByte(dictionary.Count)));
            }
            else {
                header.Add(0xD0 | 0xf);
                header.AddRange(WriteBinaryInteger(dictionary.Count, false));
            }


            foreach (int val in refs) {
                byte[] refBuffer = RegulateNullBytes(BitConverter.GetBytes(val), _objRefSize);
                Array.Reverse(refBuffer);
                buffer.InsertRange(0, refBuffer);
            }

            buffer.InsertRange(0, header);
            _objectTable.InsertRange(0, buffer);
        }

        private void ComposeBinaryArray (List<object> objects)
        {
            var buffer = new List<byte>();
            var header = new List<byte>();
            var refs = new List<int>();

            for (int i = objects.Count - 1; i >= 0; i--) {
                ComposeBinary(objects[i]);
                _offsetTable.Add(_objectTable.Count);
                refs.Add(_refCount);
                _refCount--;
            }

            if (objects.Count < 15) {
                header.Add(Convert.ToByte(0xA0 | Convert.ToByte(objects.Count)));
            }
            else {
                header.Add(0xA0 | 0xf);
                header.AddRange(WriteBinaryInteger(objects.Count, false));
            }

            foreach (int val in refs) {
                byte[] refBuffer = RegulateNullBytes(BitConverter.GetBytes(val), _objRefSize);
                Array.Reverse(refBuffer);
                buffer.InsertRange(0, refBuffer);
            }

            buffer.InsertRange(0, header);
            _objectTable.InsertRange(0, buffer);
        }

        private void ComposeBinary (object obj)
        {
            switch (obj.GetType().ToString()) {
                case "System.Collections.Generic.Dictionary`2[System.String,System.Object]":
                    WriteBinaryDictionary((Dictionary<string, object>)obj);
                    return;
                case "System.Collections.Generic.List`1[System.Object]":
                    ComposeBinaryArray((List<object>)obj);
                    return;
                case "System.Byte[]":
                    WriteBinaryByteArray((byte[])obj);
                    return;
                case "System.Double":
                    WriteBinaryDouble((double)obj);
                    return;
                case "System.Int32":
                    WriteBinaryInteger((int)obj, true);
                    return;
                case "System.String":
                    WriteBinaryString((string)obj, true);
                    return;
                case "System.DateTime":
                    WriteBinaryDate((DateTime)obj);
                    return;
                case "System.Boolean":
                    WriteBinaryBool((bool)obj);
                    return;
                default:
                    return;
            }
        }

        private void WriteBinaryDate (DateTime obj)
        {
            var buffer = new List<byte>(RegulateNullBytes(BitConverter.GetBytes(PlistDateConverter.ConvertToAppleTimeStamp(obj)), 8));
            buffer.Reverse();
            buffer.Insert(0, 0x33);
            _objectTable.InsertRange(0, buffer);
        }

        private void WriteBinaryBool (bool obj)
        {
            var buffer = new List<byte>(new[] { obj ? (byte)9 : (byte)8 });
            _objectTable.InsertRange(0, buffer);
        }

        private byte[] WriteBinaryInteger (int value, bool write)
        {
            var buffer = new List<byte>(BitConverter.GetBytes((long)value));
            buffer = new List<byte>(RegulateNullBytes(buffer.ToArray()));
            while (buffer.Count != Math.Pow(2, Math.Log(buffer.Count) / Math.Log(2)))
                buffer.Add(0);
            int header = 0x10 | (int)(Math.Log(buffer.Count) / Math.Log(2));
            buffer.Reverse();
            buffer.Insert(0, Convert.ToByte(header));
            if (write)
                _objectTable.InsertRange(0, buffer);
            return buffer.ToArray();
        }

        private void WriteBinaryDouble (double value)
        {
            var buffer = new List<byte>(RegulateNullBytes(BitConverter.GetBytes(value), 4));
            while (buffer.Count != Math.Pow(2, Math.Log(buffer.Count) / Math.Log(2)))
                buffer.Add(0);
            int header = 0x20 | (int)(Math.Log(buffer.Count) / Math.Log(2));
            buffer.Reverse();
            buffer.Insert(0, Convert.ToByte(header));
            _objectTable.InsertRange(0, buffer);
        }

        private void WriteBinaryByteArray (byte[] value)
        {
            var buffer = new List<byte>(value);
            var header = new List<byte>();
            if (value.Length < 15) {
                header.Add(Convert.ToByte(0x40 | Convert.ToByte(value.Length)));
            }
            else {
                header.Add(0x40 | 0xf);
                header.AddRange(WriteBinaryInteger(buffer.Count, false));
            }

            buffer.InsertRange(0, header);
            _objectTable.InsertRange(0, buffer);
        }

        private void WriteBinaryString (string value, bool head)
        {
            var header = new List<byte>();
            var buffer = value.ToCharArray().Select(Convert.ToByte).ToList();

            if (head) {
                if (value.Length < 15) {
                    header.Add(Convert.ToByte(0x50 | Convert.ToByte(value.Length)));
                }
                else {
                    header.Add(0x50 | 0xf);
                    header.AddRange(WriteBinaryInteger(buffer.Count, false));
                }
            }

            buffer.InsertRange(0, header);
            _objectTable.InsertRange(0, buffer);
        }

        private byte[] RegulateNullBytes (byte[] value, int minBytes = 1)
        {
            Array.Reverse(value);
            var bytes = new List<byte>(value);
            for (int i = 0; i < bytes.Count; i++) {
                if (bytes[i] != 0 || bytes.Count <= minBytes)
                    break;
                bytes.Remove(bytes[i]);
                i--;
            }

            if (bytes.Count < minBytes) {
                int dist = minBytes - bytes.Count;
                for (int i = 0; i < dist; i++)
                    bytes.Insert(0, 0);
            }

            value = bytes.ToArray();
            Array.Reverse(value);
            return value;
        }

        private void ParseTrailer (List<byte> trailer)
        {
            _offsetByteSize = BitConverter.ToInt32(RegulateNullBytes(trailer.GetRange(6, 1).ToArray(), 4), 0);
            _objRefSize = BitConverter.ToInt32(RegulateNullBytes(trailer.GetRange(7, 1).ToArray(), 4), 0);
            byte[] refCountBytes = trailer.GetRange(12, 4).ToArray();
            Array.Reverse(refCountBytes);
            _refCount = BitConverter.ToInt32(refCountBytes, 0);
            byte[] offsetTableOffsetBytes = trailer.GetRange(24, 8).ToArray();
            Array.Reverse(offsetTableOffsetBytes);
            _offsetTableOffset = BitConverter.ToInt64(offsetTableOffsetBytes, 0);
        }

        private void ParseOffsetTable (List<byte> offsetTableBytes)
        {
            for (int i = 0; i < offsetTableBytes.Count; i += _offsetByteSize) {
                byte[] buffer = offsetTableBytes.GetRange(i, _offsetByteSize).ToArray();
                Array.Reverse(buffer);
                _offsetTable.Add(BitConverter.ToInt32(RegulateNullBytes(buffer, 4), 0));
            }
        }

        private object ParseBinaryDictionary (int objRef)
        {
            var buffer = new Dictionary<string, object>();
            var refs = new List<int>();

            int refStartPosition;
            int refCount = GetCount(_offsetTable[objRef], out refStartPosition);

            if (refCount < 15)
                refStartPosition = _offsetTable[objRef] + 1;
            else
                refStartPosition = _offsetTable[objRef] + 2 + RegulateNullBytes(BitConverter.GetBytes(refCount)).Length;

            for (int i = refStartPosition; i < refStartPosition + refCount * 2 * _objRefSize; i += _objRefSize) {
                byte[] refBuffer = _objectTable.GetRange(i, _objRefSize).ToArray();
                Array.Reverse(refBuffer);
                refs.Add(BitConverter.ToInt32(RegulateNullBytes(refBuffer, 4), 0));
            }

            for (int i = 0; i < refCount; i++)
                buffer.Add((string)ParseBinary(refs[i]), ParseBinary(refs[i + refCount]));

            return buffer;
        }

        private object ParseBinaryArray (int objRef)
        {
            var buffer = new List<object>();
            var refs = new List<int>();

            int refStartPosition;
            int refCount = GetCount(_offsetTable[objRef], out refStartPosition);

            if (refCount < 15)
                refStartPosition = _offsetTable[objRef] + 1;
            else
                //The following integer has a header aswell so we increase the refStartPosition by two to account for that.
                refStartPosition = _offsetTable[objRef] + 2 + RegulateNullBytes(BitConverter.GetBytes(refCount)).Length;

            for (int i = refStartPosition; i < refStartPosition + refCount * _objRefSize; i += _objRefSize) {
                byte[] refBuffer = _objectTable.GetRange(i, _objRefSize).ToArray();
                Array.Reverse(refBuffer);
                refs.Add(BitConverter.ToInt32(RegulateNullBytes(refBuffer, 4), 0));
            }

            for (int i = 0; i < refCount; i++)
                buffer.Add(ParseBinary(refs[i]));

            return buffer;
        }

        private int GetCount (int bytePosition, out int newBytePosition)
        {
            byte headerByte = _objectTable[bytePosition];
            byte headerByteTrail = Convert.ToByte(headerByte & 0xf);
            int count;
            if (headerByteTrail < 15) {
                count = headerByteTrail;
                newBytePosition = bytePosition + 1;
            }
            else
                count = (int)ParseBinaryInt(bytePosition + 1, out newBytePosition);
            return count;
        }

        private object ParseBinary (int objRef)
        {
            byte header = _objectTable[_offsetTable[objRef]];
            switch (header & 0xF0) {
                case 0:
                    //If the byte is
                    //0 return null
                    //9 return true
                    //8 return false
                    return _objectTable[_offsetTable[objRef]] == 0 ? (object)null : _objectTable[_offsetTable[objRef]] == 9;
                case 0x10:
                    return ParseBinaryInt(_offsetTable[objRef]);
                case 0x20:
                    return ParseBinaryReal(_offsetTable[objRef]);
                case 0x30:
                    return ParseBinaryDate(_offsetTable[objRef]);
                case 0x40:
                    return ParseBinaryByteArray(_offsetTable[objRef]);
                case 0x50: //String ASCII
                    return ParseBinaryAsciiString(_offsetTable[objRef]);
                case 0x60: //String Unicode
                    return ParseBinaryUnicodeString(_offsetTable[objRef]);
                case 0xD0:
                    return ParseBinaryDictionary(objRef);
                case 0xA0:
                    return ParseBinaryArray(objRef);
            }
            throw new PlistFormatException(string.Format("Type with header 0x{0:x} is not supported.", header));
        }

        private object ParseBinaryDate (int headerPosition)
        {
            byte[] buffer = _objectTable.GetRange(headerPosition + 1, 8).ToArray();
            Array.Reverse(buffer);
            double appleTime = BitConverter.ToDouble(buffer, 0);
            DateTime result = PlistDateConverter.ConvertFromAppleTimeStamp(appleTime);
            return result;
        }

        private object ParseBinaryInt (int headerPosition)
        {
            int output;
            return ParseBinaryInt(headerPosition, out output);
        }

        private object ParseBinaryInt (int headerPosition, out int newHeaderPosition)
        {
            byte header = _objectTable[headerPosition];
            var byteCount = (int)Math.Pow(2, header & 0xf);
            byte[] buffer = _objectTable.GetRange(headerPosition + 1, byteCount).ToArray();
            Array.Reverse(buffer);
            //Add one to account for the header byte
            newHeaderPosition = headerPosition + byteCount + 1;
            return BitConverter.ToInt32(RegulateNullBytes(buffer, 4), 0);
        }

        private object ParseBinaryReal (int headerPosition)
        {
            byte header = _objectTable[headerPosition];
            var byteCount = (int)Math.Pow(2, header & 0xf);
            byte[] buffer = _objectTable.GetRange(headerPosition + 1, byteCount).ToArray();
            Array.Reverse(buffer);
            return BitConverter.ToDouble(RegulateNullBytes(buffer, 8), 0);
        }

        private object ParseBinaryAsciiString (int headerPosition)
        {
            int charStartPosition;
            int charCount = GetCount(headerPosition, out charStartPosition);
            var buffer = _objectTable.GetRange(charStartPosition, charCount);
            return buffer.Count > 0 ? Encoding.ASCII.GetString(buffer.ToArray()) : string.Empty;
        }

        private object ParseBinaryUnicodeString (int headerPosition)
        {
            int charStartPosition;
            int charCount = GetCount(headerPosition, out charStartPosition);
            charCount = charCount * 2;
            var buffer = new byte[charCount];

            for (int i = 0; i < charCount; i += 2) {
                byte one = _objectTable.GetRange(charStartPosition + i, 1)[0];
                byte two = _objectTable.GetRange(charStartPosition + i + 1, 1)[0];

                if (BitConverter.IsLittleEndian) {
                    buffer[i] = two;
                    buffer[i + 1] = one;
                }
                else {
                    buffer[i] = one;
                    buffer[i + 1] = two;
                }
            }

            return Encoding.Unicode.GetString(buffer);
        }

        private object ParseBinaryByteArray (int headerPosition)
        {
            int byteStartPosition;
            int byteCount = GetCount(headerPosition, out byteStartPosition);
            return _objectTable.GetRange(byteStartPosition, byteCount).ToArray();
        }
    }
}