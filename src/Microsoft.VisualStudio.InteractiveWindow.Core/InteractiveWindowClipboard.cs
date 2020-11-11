// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections;
using System.Collections.Generic;

namespace Microsoft.VisualStudio.InteractiveWindow
{
    internal abstract class InteractiveWindowClipboard
    {
        internal abstract bool ContainsData(string format);

        internal abstract object GetData(string format);

        internal abstract bool ContainsText();

        internal abstract string GetText();

        internal abstract void SetDataObject(IEnumerable<(string format, object value)> data, bool copy);

        internal abstract (bool, bool) IsDataTypePresent(string format1, string format2);
    }
}
