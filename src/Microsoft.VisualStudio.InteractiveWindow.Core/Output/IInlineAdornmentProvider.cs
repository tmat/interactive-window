// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Microsoft.VisualStudio.Text.Editor;

namespace Microsoft.VisualStudio.InteractiveWindow
{
    internal interface IInlineAdornmentProvider
    {
        void AddInlineAdornment(ITextView view, object uiElement); 
        void ZoomInlineAdornments(ITextView view, double zoomFactor);
        void MinimizeLastInlineAdornment(ITextView view);
        void RemoveAllAdornments(ITextView view);
    }
}
