// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.ComponentModel.Composition;
using System.Windows.Media;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;

namespace Microsoft.VisualStudio.InteractiveWindow
{
    [Export(typeof(EditorFormatDefinition))]
    [ClassificationType(ClassificationTypeNames = OutputClassifierProvider.ErrorOutputFormatDefinitionName)]
    [Name(OutputClassifierProvider.ErrorOutputFormatDefinitionName)]
    [UserVisible(true)]
    internal sealed class ErrorOutputFormatDefinition : ClassificationFormatDefinition
    {
        [Export]
        [Name(OutputClassifierProvider.ErrorOutputFormatDefinitionName)]
        [BaseDefinition(PredefinedClassificationTypeNames.NaturalLanguage)]
        internal static readonly ClassificationTypeDefinition Definition = null;

        public ErrorOutputFormatDefinition()
        {
            ForegroundColor = Color.FromRgb(0xff, 0, 0);
            DisplayName = InteractiveWindowResources.ErrorOutputFormatDefinitionDisplayName;
        }
    }
}
