// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Windows;

namespace Microsoft.VisualStudio.InteractiveWindow
{
    internal sealed class SystemClipboard : InteractiveWindowClipboard
    {
        internal override bool ContainsData(string format) => Clipboard.ContainsData(format);

        internal override object GetData(string format) => Clipboard.GetData(format);

        internal override bool ContainsText() => Clipboard.ContainsText();

        internal override string GetText() => Clipboard.GetText();

        internal override void SetDataObject(string text, string blocks, string rtf, bool lineCutCopyTag, bool boxCutCopyTag)
        {
            var data = new DataObject();
            data.SetData(DataFormats.Text, text);
            data.SetData(DataFormats.StringFormat, text);
            data.SetData(DataFormats.UnicodeText, text);
            data.SetData(InteractiveClipboardFormat.Tag, blocks);

            if (rtf != null)
            {
                data.SetData(DataFormats.Rtf, rtf);
            }

            // tag the data in the clipboard if requested
            if (lineCutCopyTag)
            {
                data.SetData(ClipboardLineBasedCutCopyTag, true);
            }

            if (boxCutCopyTag)
            {
                data.SetData(BoxSelectionCutCopyTag, true);
            }

            Clipboard.SetDataObject(data, copy: true);
        }

        internal override (bool, bool) IsDataTypePresent(string format1, string format2)
        {
            var data = Clipboard.GetDataObject();
            return (data.GetDataPresent(format1), data.GetDataPresent(format2));
        }
    }
}
