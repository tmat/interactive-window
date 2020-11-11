// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Microsoft.VisualStudio.Text.Editor;

namespace Microsoft.VisualStudio.InteractiveWindow
{
    internal interface ICursorUpdater
    {
        void ResetCursor(ITextView textView);
        void SetWaitCursor(ITextView textView);
    }
}
