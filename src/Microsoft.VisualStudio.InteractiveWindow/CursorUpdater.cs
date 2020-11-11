// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using Microsoft.VisualStudio.Text.Editor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Input;

namespace Microsoft.VisualStudio.InteractiveWindow
{
    internal sealed class CursorUpdater : ICursorUpdater
    {
        private Cursor _oldCursor;

        public void ResetCursor(ITextView textView)
        {
            var view = (ContentControl)textView;
            
            if (_oldCursor != null)
            {
                view.Cursor = _oldCursor;
            }

            _oldCursor = null;
        }

        public void SetWaitCursor(ITextView textView)
        {
            var view = (ContentControl)textView;

            // Save the old value of the cursor so it can be restored
            // after execution has finished
            _oldCursor = view.Cursor;

            // TODO: Design work to come up with the correct cursor to use
            // Set the repl's cursor to the "executing" cursor
            view.Cursor = Cursors.Wait;
        }
    }
}
