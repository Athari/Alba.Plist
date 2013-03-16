Alba.Plist
==========

Description
-----------

This is a Mac OS X Property List (Plist) serialization library written in C#. It supports both XML and binary versions of the plist format.

| plist      | C#                         |
|------------|----------------------------|
| string     | string                     |
| integer    | short, int, long           |
| real       | double                     |
| dictionary | Dictionary<string, object> |
| array      | List<object>               |
| date       | DateTime                   |
| data       | List<byte>                 |
| boolean    | bool                       |

Usage
-----

See `PlistCS/PlistCS/plistTests.cs` for examples of reading and writing all types to both XML and binary. E.g. to read a plist from disk whose root node is a dictionary:

```cs
    Dictionary<string, object> dict = (Dictionary<string, object>)Plist.readPlist("testBin.plist");
```

The plist format (binary or XML) is automatically detected so call the same readPlist method for XML

```cs
    Dictionary<string, object> dict = (Dictionary<string, object>)Plist.readPlist("testXml.plist");
```

To write a plist, e.g. dictionary

```cs
    Dictionary<string, object> dict = new Dictionary<string, object>
    {
        { "String Example", "Hello There" },
        { "Integer Example", 1234 }
    };
    Plist.writeXml(dict, "xmlTarget.plist");
```

and for a binary plist

```cs
    Dictionary<string, object> dict = new Dictionary<string, object>
    {
        { "String Example", "Hello There" },
        { "Integer Example", 1234 }
    };
    Plist.writeBinary(dict, "xmlTarget.plist");
```

Other public methods allow reading and writing from streams and byte arrays.

License
-------

MIT License

Copyright © 2011, Mark Tilton, Animetrics Inc.

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
