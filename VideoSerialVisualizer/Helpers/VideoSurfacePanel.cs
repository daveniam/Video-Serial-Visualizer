// Video Serial Visualizer - Copyright (C) 2026  David Nieves
// SPDX-License-Identifier: GPL-3.0-or-later
// Software libre, sin garantia alguna. Ver LICENSE para los terminos completos.

using System.Windows.Forms;

namespace VideoSerialVisualizer.Helpers;

/// <summary>
/// Panel nativo que sirve de superficie de render para LibVLC y que ademas detecta clics.
///
/// LibVLC crea su propia ventana hija dentro de este panel para dibujar el video, asi que los
/// clics del usuario caen en esa hija y nunca llegan al panel. Windows avisa al padre de esos
/// clics con WM_PARENTNOTIFY: se escucha ese mensaje para saber que se hizo clic sobre el video.
/// </summary>
public class VideoSurfacePanel : Panel
{
    private const int WM_PARENTNOTIFY = 0x0210;
    private const int WM_LBUTTONDOWN = 0x0201;

    /// <summary>Se dispara con un clic izquierdo sobre el panel o sobre el video que contiene.</summary>
    public event EventHandler? VideoClicked;

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_PARENTNOTIFY && ((int)m.WParam & 0xFFFF) == WM_LBUTTONDOWN)
            VideoClicked?.Invoke(this, EventArgs.Empty);

        base.WndProc(ref m);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        // Clic en una zona del panel no cubierta por el video (bandas negras del letterbox).
        if (e.Button == MouseButtons.Left)
            VideoClicked?.Invoke(this, EventArgs.Empty);

        base.OnMouseDown(e);
    }
}
