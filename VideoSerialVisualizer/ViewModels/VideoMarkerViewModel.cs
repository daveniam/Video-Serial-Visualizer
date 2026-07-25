// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Windows.Documents;
using CommunityToolkit.Mvvm.ComponentModel;
using Markdig;
using VideoSerialVisualizer.Helpers;
using VideoSerialVisualizer.Models;

namespace VideoSerialVisualizer.ViewModels;

/// <summary>
/// Envoltorio de una etiqueta de linea de tiempo para la vista: agrega el renderizado Markdown
/// (para el tooltip de vista previa) y la posicion como porcentaje (para dibujarla en la barra).
/// </summary>
public partial class VideoMarkerViewModel : ObservableObject
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    public int Id { get; }

    public long TimeMs { get; }

    [ObservableProperty]
    private string texto;

    private readonly long _durationMs;

    public VideoMarkerViewModel(VideoMarker marker, long durationMs)
    {
        Id = marker.Id;
        TimeMs = marker.TimeMs;
        texto = marker.Texto;
        _durationMs = durationMs;
    }

    /// <summary>Posicion como porcentaje del video (0-100), para ubicar el marcador en la barra.</summary>
    public double Percent => _durationMs > 0 ? Math.Clamp((double)TimeMs / _durationMs * 100.0, 0, 100) : 0;

    public string TimeText => TimeFormatter.Format(TimeMs);

    /// <summary>Documento renderizado a partir del Markdown crudo de <see cref="Texto"/>, para el
    /// tooltip de vista previa. Se recalcula cada vez que el texto cambia (edicion).</summary>
    public FlowDocument RenderedDocument => Markdig.Wpf.Markdown.ToFlowDocument(Texto, Pipeline);

    partial void OnTextoChanged(string value) => OnPropertyChanged(nameof(RenderedDocument));
}
