// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace Microsoft.VisualStudio.InteractiveWindow
{
    /// <summary>
    /// Classifies error text in interactive window output.
    /// </summary>
    [Export(typeof(IClassifierProvider))]
    [ContentType(PredefinedInteractiveContentTypes.InteractiveOutputContentTypeName)]
    internal sealed partial class OutputClassifierProvider : IClassifierProvider
    {
        internal const string ErrorOutputFormatDefinitionName = "Interactive Window Error Output";

        private static readonly object s_textBufferPropertyKey = new object();

        [Import]
        private IClassificationTypeRegistryService _classificationRegistry = null;

        public IClassifier GetClassifier(ITextBuffer textBuffer)
        {
            return new Classifier(
                textBuffer,
                _classificationRegistry.GetClassificationType(ErrorOutputFormatDefinitionName));
        }

        internal static void AttachToBuffer(ITextBuffer buffer, SortedSpans spans)
        {
            buffer.Properties[s_textBufferPropertyKey] = spans;
        }

        internal static void ClearSpans(ITextBuffer buffer)
        {
            SortedSpans errorSpans;
            if (buffer.Properties.TryGetProperty(s_textBufferPropertyKey, out errorSpans))
            {
                errorSpans.Clear();
            }
        }

        private sealed class Classifier : IClassifier
        {
            private readonly ITextBuffer _buffer;
            private readonly IClassificationType _errorOutputType;

            public Classifier(ITextBuffer buffer, IClassificationType errorOutputType)
            {
                _errorOutputType = errorOutputType;
                _buffer = buffer;
            }

            public IList<ClassificationSpan> GetClassificationSpans(SnapshotSpan span)
            {
                SortedSpans errorSpans;
                if (!_buffer.Properties.TryGetProperty(s_textBufferPropertyKey, out errorSpans))
                {
                    return Array.Empty<ClassificationSpan>();
                }

                List<ClassificationSpan> classifications = new List<ClassificationSpan>();

                foreach (var overlap in errorSpans.GetOverlap(span.Span))
                {
                    classifications.Add(new ClassificationSpan(new SnapshotSpan(span.Snapshot, overlap), _errorOutputType));
                }

                return classifications;
            }

            public event EventHandler<ClassificationChangedEventArgs> ClassificationChanged
            {
                add { }
                remove { }
            }
        }
    }
}
