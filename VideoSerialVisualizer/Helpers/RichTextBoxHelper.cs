// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace VideoSerialVisualizer.Helpers;

/// <summary>
/// RichTextBox.Document NO es una DependencyProperty (FlowDocument tiene semantica de "un solo
/// dueno" que no encaja con el modelo estandar de propiedad bindeable), asi que WPF no permite
/// escribir Document="{Binding ...}" directo en XAML: tira "A 'Binding' cannot be set on the
/// 'Document' property... can only be set on a DependencyProperty" en cuanto se aplica el template.
///
/// Esta propiedad adjunta hace de puente: ELLA si es una DependencyProperty (se puede bindear sin
/// problema), y en su callback asigna el FlowDocument recibido a RichTextBox.Document.
/// </summary>
public static class RichTextBoxHelper
{
    public static readonly DependencyProperty BoundDocumentProperty =
        DependencyProperty.RegisterAttached(
            "BoundDocument",
            typeof(FlowDocument),
            typeof(RichTextBoxHelper),
            new PropertyMetadata(null, OnBoundDocumentChanged));

    public static FlowDocument? GetBoundDocument(DependencyObject obj) => (FlowDocument?)obj.GetValue(BoundDocumentProperty);

    public static void SetBoundDocument(DependencyObject obj, FlowDocument? value) => obj.SetValue(BoundDocumentProperty, value);

    private static void OnBoundDocumentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is RichTextBox richTextBox)
            richTextBox.Document = e.NewValue as FlowDocument ?? new FlowDocument();
    }
}
