using System;
using System.Runtime.Serialization;

namespace Alba.Plist
{
    [Serializable]
    public class PlistFormatException : Exception
    {
        public PlistFormatException ()
        {}

        public PlistFormatException (string message) : base(message)
        {}

        public PlistFormatException (string message, Exception inner) : base(message, inner)
        {}

        protected PlistFormatException (SerializationInfo info, StreamingContext context) : base(info, context)
        {}
    }
}