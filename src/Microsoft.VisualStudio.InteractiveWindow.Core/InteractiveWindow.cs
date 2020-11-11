// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

// Dumps commands in QueryStatus and Exec.
// #define DUMP_COMMANDS

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Text.Projection;
using Microsoft.VisualStudio.Utilities;

namespace Microsoft.VisualStudio.InteractiveWindow
{
    // TODO: We should condense committed language buffers into a single language buffer and save the
    // classifications from the previous language buffer if the perf of having individual buffers
    // starts having problems w/ a large number of inputs.

    /// <summary>
    /// Provides implementation of a Repl Window built on top of the VS editor using projection buffers.
    /// </summary>
    internal partial class InteractiveWindow : IInteractiveWindow2, IInteractiveWindowOperations2
    {
        // The following two field definitions have to stay in sync with VS editor implementation

        /// <summary>
        /// A data format used to tag the contents of the clipboard so that it's clear
        /// the data has been put in the clipboard by our editor
        /// </summary>
        internal const string ClipboardLineBasedCutCopyTag = "VisualStudioEditorOperationsLineCutCopyClipboardTag";

        /// <summary>
        /// A data format used to tag the contents of the clipboard as a box selection.
        /// This is the same string that was used in VS9 and previous versions.
        /// </summary>
        internal const string BoxSelectionCutCopyTag = "MSDEVColumnSelect";

        public event EventHandler<SubmissionBufferAddedEventArgs> SubmissionBufferAdded;

        PropertyCollection IPropertyOwner.Properties { get; } = new PropertyCollection();

        private readonly SemaphoreSlim _inputReaderSemaphore = new SemaphoreSlim(initialCount: 1, maxCount: 1);

        /// <remarks>
        /// WARNING: Members of this object should only be accessed from the UI thread.
        /// </remarks>
        private readonly UIThreadOnly _uiOnly;
        private readonly IMainThreadDispatcher _dispatcher;

        // Setter for InteractiveWindowClipboard is a test hook.  
        internal InteractiveWindowClipboard InteractiveWindowClipboard { get; }

        #region Initialization

        public InteractiveWindow(
            IInteractiveWindowEditorFactoryService editorFactory,
            IContentTypeRegistryService contentTypeRegistry,
            ITextBufferFactoryService bufferFactory,
            IProjectionBufferFactoryService projectionBufferFactory,
            IEditorOperationsFactoryService editorOperationsFactory,
            ITextBufferUndoManagerProvider textBufferUndoManagerProvider,
            IRtfBuilderService rtfBuilderService,
            ISmartIndentationService smartIndenterService,
            IInlineAdornmentProvider adornmentProvider,
            ICursorUpdater cursorUpdater,
            InteractiveWindowClipboard clipboard,
            IInteractiveEvaluator evaluator,
            IMainThreadDispatcher dispatcher,
            IUIThreadOperationExecutor waitIndicator)
        {
            if (evaluator == null)
            {
                throw new ArgumentNullException(nameof(evaluator));
            }
            
            _dispatcher = dispatcher;
            _uiOnly = new UIThreadOnly(
                this,
                editorFactory,
                contentTypeRegistry,
                bufferFactory,
                projectionBufferFactory,
                editorOperationsFactory,
                textBufferUndoManagerProvider,
                rtfBuilderService,
                smartIndenterService,
                adornmentProvider,
                cursorUpdater,
                evaluator,
                waitIndicator);

            InteractiveWindowClipboard = clipboard;
            evaluator.CurrentWindow = this;

            RequiresUIThread();
        }

        async Task<ExecutionResult> IInteractiveWindow.InitializeAsync()
        {
            try
            {
                RequiresUIThread();
                var uiOnly = _uiOnly; // Verified above.

                if (uiOnly.State != State.Starting)
                {
                    throw new InvalidOperationException(InteractiveWindowResources.AlreadyInitialized);
                }

                uiOnly.State = State.Initializing;

                // Anything that reads options should wait until after this call so the evaluator can set the options first
                ExecutionResult result = await uiOnly.Evaluator.InitializeAsync().ConfigureAwait(continueOnCapturedContext: true);
                Debug.Assert(_dispatcher.IsExecutingOnMainThread); // ConfigureAwait should bring us back to the UI thread.

                if (result.IsSuccessful)
                {
                    uiOnly.PrepareForInput();
                }

                return result;
            }
            catch (Exception e) when (ReportAndPropagateException(e))
            {
                throw ExceptionUtilities.Unreachable;
            }
        }

        private bool ReportAndPropagateException(Exception e)
        {
            FatalError.ReportWithoutCrashUnlessCanceled(e); // Drop return value.

            ((IInteractiveWindow)this).WriteErrorLine(InteractiveWindowResources.InternalError);

            return false; // Never consider the exception handled.
        }

        #endregion

        void IInteractiveWindow.Close()
        {
            _dispatcher.ExecuteOnMainThread(() => _uiOnly.Close());
        }

        #region Misc Helpers

        /// <remarks>
        /// The caller is responsible for using the buffer in a thread-safe manner.
        /// </remarks>
        public ITextBuffer CurrentLanguageBuffer => _uiOnly.CurrentLanguageBuffer;

        void IDisposable.Dispose()
        {
            _dispatcher.ExecuteOnMainThread(() => ((IDisposable)_uiOnly).Dispose());
        }

        public static InteractiveWindow FromBuffer(ITextBuffer buffer)
        {
            object result;
            buffer.Properties.TryGetProperty(typeof(InteractiveWindow), out result);
            return result as InteractiveWindow;
        }

        #endregion

        #region IInteractiveWindow

        public event Action ReadyForInput;

        /// <remarks>
        /// The caller is responsible for using the text view in a thread-safe manner.
        /// </remarks>
        ITextView IInteractiveWindow.TextView => _uiOnly.TextView;

        /// <remarks>
        /// The caller is responsible for using the buffer in a thread-safe manner.
        /// </remarks>
        ITextBuffer IInteractiveWindow.OutputBuffer => _uiOnly.OutputBuffer;

        /// <remarks>
        /// The caller is responsible for using the writer in a thread-safe manner.
        /// </remarks>
        TextWriter IInteractiveWindow.OutputWriter => _uiOnly.OutputWriter;

        /// <remarks>
        /// The caller is responsible for using the writer in a thread-safe manner.
        /// </remarks>
        TextWriter IInteractiveWindow.ErrorOutputWriter => _uiOnly.ErrorOutputWriter;

        /// <remarks>
        /// The caller is responsible for using the evaluator in a thread-safe manner.
        /// </remarks>
        IInteractiveEvaluator IInteractiveWindow.Evaluator => _uiOnly.Evaluator;

        /// <remarks>
        /// Normally, an async method would have an NFW exception filter.  This
        /// one doesn't because it just calls other async methods that already
        /// have filters.
        /// </remarks>
        async Task IInteractiveWindow.SubmitAsync(IEnumerable<string> inputs)
        {
            var completion = new TaskCompletionSource<object>();
            var submissions = inputs.ToArray();
            var numSubmissions = submissions.Length;
            PendingSubmission[] pendingSubmissions = new PendingSubmission[numSubmissions];
            if (numSubmissions == 0)
            {
                completion.SetResult(null);
            }
            else
            {
                for (int i = 0; i < numSubmissions; i++)
                {
                    pendingSubmissions[i] = new PendingSubmission(submissions[i], i == numSubmissions - 1 ? completion : null);
                }
            }

            _dispatcher.ExecuteOnMainThread(() => _uiOnly.Submit(pendingSubmissions));

            // This indicates that the last submission has completed.
            await completion.Task.ConfigureAwait(false);

            // These should all have finished already, but we'll await them so that their
            // statuses are folded into the task we return.
#pragma warning disable VSTHRD003 // Avoid awaiting foreign Tasks
            await Task.WhenAll(pendingSubmissions.Select(p => p.Task)).ConfigureAwait(false);
#pragma warning restore
        }

        void IInteractiveWindow.AddInput(string command)
        {
            _dispatcher.ExecuteOnMainThread(() => _uiOnly.AddInput(command));
        }

        void IInteractiveWindow2.AddToHistory(string input)
        {
            _dispatcher.ExecuteOnMainThread(() => _uiOnly.AddToHistory(input));
        }

        void IInteractiveWindow.FlushOutput()
        {
            _dispatcher.ExecuteOnMainThread(() => _uiOnly.FlushOutput());
        }

        void IInteractiveWindow.InsertCode(string text)
        {
            _dispatcher.ExecuteOnMainThread(() => _uiOnly.InsertCode(text));
        }

        #endregion

        #region Commands

        Task<ExecutionResult> IInteractiveWindowOperations.ResetAsync(bool initialize)
        {
            return _dispatcher.ExecuteOnMainThread(() => _uiOnly.ResetAsync(initialize));
        }

        void IInteractiveWindowOperations.ClearHistory()
        {
            _dispatcher.ExecuteOnMainThread(() => _uiOnly.ClearHistory());
        }

        void IInteractiveWindowOperations.ClearView()
        {
            _dispatcher.ExecuteOnMainThread(() => _uiOnly.ClearView());
        }

        /// <summary>
        /// Pastes from the clipboard into the text view
        /// </summary>
        bool IInteractiveWindowOperations.Paste()
        {
            return _dispatcher.ExecuteOnMainThread(() => _uiOnly.Paste());
        }

        void IInteractiveWindowOperations.ExecuteInput()
        {
            _ = _dispatcher.ExecuteOnMainThread(() => _uiOnly.ExecuteInputAsync());
        }

        /// <remarks>
        /// Test hook.
        /// </remarks>
        internal Task ExecuteInputAsync()
        {
            return _dispatcher.ExecuteOnMainThread(() => _uiOnly.ExecuteInputAsync());
        }

        /// <summary>
        /// Appends text to the output buffer and updates projection buffer to include it.
        /// WARNING: this has to be the only method that writes to the output buffer so that 
        /// the output buffering counters are kept in sync.
        /// </summary>
        internal void AppendOutput(IEnumerable<string> output)
        {
            RequiresUIThread();
            _uiOnly.AppendOutput(output); // Verified above.
        }

        /// <summary>
        /// Clears the current input
        /// </summary>
        void IInteractiveWindowOperations.Cancel()
        {
            _dispatcher.ExecuteOnMainThread(() => _uiOnly.Cancel());
        }

        void IInteractiveWindowOperations.HistoryPrevious(string search)
        {
            _dispatcher.ExecuteOnMainThread(() => _uiOnly.HistoryPrevious(search));
        }

        void IInteractiveWindowOperations.HistoryNext(string search)
        {
            _dispatcher.ExecuteOnMainThread(() => _uiOnly.HistoryNext(search));
        }

        void IInteractiveWindowOperations.HistorySearchNext()
        {
            _dispatcher.ExecuteOnMainThread(() => _uiOnly.HistorySearchNext());
        }

        void IInteractiveWindowOperations.HistorySearchPrevious()
        {
            _dispatcher.ExecuteOnMainThread(() => _uiOnly.HistorySearchPrevious());
        }

        /// <summary>
        /// Moves to the beginning of the line.
        /// </summary>
        void IInteractiveWindowOperations.Home(bool extendSelection)
        {
            _dispatcher.ExecuteOnMainThread(() => _uiOnly.Home(extendSelection));
        }

        /// <summary>
        /// Moves to the end of the line.
        /// </summary>
        void IInteractiveWindowOperations.End(bool extendSelection)
        {
            _dispatcher.ExecuteOnMainThread(() => _uiOnly.End(extendSelection));
        }

        void IInteractiveWindowOperations.SelectAll()
        {
            _dispatcher.ExecuteOnMainThread(() => _uiOnly.SelectAll());
        }

        #endregion

        #region Keyboard Commands

        /// <remarks>Only consistent on the UI thread.</remarks>
        bool IInteractiveWindow.IsRunning => _uiOnly.State != State.WaitingForInput;

        /// <remarks>Only consistent on the UI thread.</remarks>
        bool IInteractiveWindow.IsResetting => _uiOnly.State == State.Resetting || _uiOnly.State == State.ResettingAndReadingStandardInput;

        /// <remarks>Only consistent on the UI thread.</remarks>
        bool IInteractiveWindow.IsInitializing => _uiOnly.State == State.Starting || _uiOnly.State == State.Initializing;

        IInteractiveWindowOperations IInteractiveWindow.Operations => this;

        bool IInteractiveWindowOperations.Delete()
        {
            return _dispatcher.ExecuteOnMainThread(() => _uiOnly.Delete());
        }

        void IInteractiveWindowOperations.Cut()
        {
            _dispatcher.ExecuteOnMainThread(() => _uiOnly.Cut());
        }

        void IInteractiveWindowOperations2.Copy()
        {
            _dispatcher.ExecuteOnMainThread(() => _uiOnly.Copy());
        }

        void IInteractiveWindowOperations2.CopyCode()
        {
            _dispatcher.ExecuteOnMainThread(() => _uiOnly.CopyCode());
        }

        bool IInteractiveWindowOperations.Backspace()
        {
            return _dispatcher.ExecuteOnMainThread(() => _uiOnly.Backspace());
        }

        bool IInteractiveWindowOperations.TrySubmitStandardInput()
        {
            return _dispatcher.ExecuteOnMainThread(() => _uiOnly.TrySubmitStandardInput());
        }

        bool IInteractiveWindowOperations.BreakLine()
        {
            return _dispatcher.ExecuteOnMainThread(() => _uiOnly.BreakLine());
        }

        bool IInteractiveWindowOperations.Return()
        {
            return _dispatcher.ExecuteOnMainThread(() => _uiOnly.Return());
        }

        void IInteractiveWindowOperations2.DeleteLine()
        {
            _dispatcher.ExecuteOnMainThread(() => _uiOnly.DeleteLine());
        }

        void IInteractiveWindowOperations2.CutLine()
        {
            _dispatcher.ExecuteOnMainThread(() => _uiOnly.CutLine());
        }

        void IInteractiveWindowOperations2.TypeChar(char typedChar)
        {
            _dispatcher.ExecuteOnMainThread(() => _uiOnly.TypeChar(typedChar));
        }

        #endregion

        #region Command Debugging

#if DUMP_COMMANDS
        private static void DumpCmd(string prefix, int result, ref Guid pguidCmdGroup, uint cmd, uint cmdf)
        {
            string cmdName;
            if (pguidCmdGroup == VSConstants.GUID_VSStandardCommandSet97)
            {
                cmdName = ((VSConstants.VSStd97CmdID)cmd).ToString();
            }
            else if (pguidCmdGroup == VSConstants.VSStd2K)
            {
                cmdName = ((VSConstants.VSStd2KCmdID)cmd).ToString();
            }
            else if (pguidCmdGroup == VSConstants.VsStd2010)
            {
                cmdName = ((VSConstants.VSStd2010CmdID)cmd).ToString();
            }
            else if (pguidCmdGroup == GuidList.guidReplWindowCmdSet)
            {
                cmdName = ((ReplCommandId)cmd).ToString();
            }
            else
            {
                return;
            }

            Debug.WriteLine("{3}({0}) -> {1}  {2}", cmdName, Enum.Format(typeof(OLECMDF), (OLECMDF)cmdf, "F"), result, prefix);
        }
#endif

        #endregion

        #region Active Code and Standard Input

        TextReader IInteractiveWindow.ReadStandardInput()
        {
            // shouldn't be called on the UI thread because we'll hang
            RequiresNonUIThread();

#pragma warning disable VSTHRD002 // Avoid problematic synchronous waits
            return ReadStandardInputAsync().GetAwaiter().GetResult();
#pragma warning restore
        }

        private async Task<TextReader> ReadStandardInputAsync()
        {
            try
            {
                // True because this is a public API and we want to use the same
                // thread as the caller (esp for blocking).
                await _inputReaderSemaphore.WaitAsync().ConfigureAwait(true); // Only one thread can read from standard input at a time.
                try
                {
                    return await _dispatcher.ExecuteOnMainThread(() => _uiOnly.ReadStandardInputAsync()).ConfigureAwait(true);
                }
                finally
                {
                    _inputReaderSemaphore.Release();
                }
            }
            catch (Exception e) when (ReportAndPropagateException(e))
            {
                throw ExceptionUtilities.Unreachable;
            }
        }

        #endregion

        #region Output

        Span IInteractiveWindow.Write(string text)
        {
            return _dispatcher.ExecuteOnMainThread(() => _uiOnly.Write(text));
        }

        Span IInteractiveWindow.WriteLine(string text)
        {
            return _dispatcher.ExecuteOnMainThread(() => _uiOnly.WriteLine(text));
        }

        Span IInteractiveWindow.WriteError(string text)
        {
            return _dispatcher.ExecuteOnMainThread(() => _uiOnly.WriteError(text));
        }

        Span IInteractiveWindow.WriteErrorLine(string text)
        {
            return _dispatcher.ExecuteOnMainThread(() => _uiOnly.WriteErrorLine(text));
        }

        void IInteractiveWindow.Write(object uiElement)
        {
            _dispatcher.ExecuteOnMainThread(() => _uiOnly.Write(uiElement));
        }

        #endregion

        #region UI Dispatcher Helpers

        private void RequiresUIThread()
        {
            if (!_dispatcher.IsExecutingOnMainThread)
            {
                throw new InvalidOperationException(InteractiveWindowResources.RequireUIThread);
            }
        }

        private void RequiresNonUIThread()
        {
            if (_dispatcher.IsExecutingOnMainThread)
            {
                throw new InvalidOperationException(InteractiveWindowResources.RequireNonUIThread);
            }
        }

        #endregion

        #region Testing

        internal event Action<State> StateChanged;

        internal void Undo_TestOnly(int count)
        {
            _dispatcher.ExecuteOnMainThread(() => _uiOnly.UndoHistory?.Undo(count));
        }

        internal void Redo_TestOnly(int count)
        {
            _dispatcher.ExecuteOnMainThread(() => _uiOnly.UndoHistory?.Redo(count));
        }

        #endregion
    }
}
