using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Miashot
{
    internal enum AnnotationTool
    {
        None,
        Rectangle,
        Arrow,
        Pen,
        Mosaic,
        Text
    }

    internal sealed class CaptureImageEventArgs : EventArgs
    {
        internal readonly Bitmap Image;
        internal string SavedPath;
        internal CaptureImageEventArgs(Bitmap image) { Image = image; }
    }

    internal abstract class Annotation : IDisposable
    {
        internal abstract void Draw(Graphics graphics);
        public virtual void Dispose() { }
    }

    internal sealed class RectangleAnnotation : Annotation
    {
        internal Rectangle Bounds;
        internal Color Color;
        internal float Width;
        internal override void Draw(Graphics graphics)
        {
            using (var pen = new Pen(Color, Width))
                graphics.DrawRectangle(pen, Bounds.X, Bounds.Y,
                    Math.Max(1, Bounds.Width), Math.Max(1, Bounds.Height));
        }
    }

    internal sealed class ArrowAnnotation : Annotation
    {
        internal Point Start;
        internal Point End;
        internal Color Color;
        internal float Width;
        internal override void Draw(Graphics graphics)
        {
            using (var pen = new Pen(Color, Width))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.ArrowAnchor;
                pen.CustomEndCap = new AdjustableArrowCap(Math.Max(4f, Width * 2.4f),
                    Math.Max(5f, Width * 3.2f), true);
                graphics.DrawLine(pen, Start, End);
            }
        }
    }

    internal sealed class PenAnnotation : Annotation
    {
        internal readonly List<Point> Points = new List<Point>();
        internal Color Color;
        internal float Width;
        internal override void Draw(Graphics graphics)
        {
            if (Points.Count < 2) return;
            using (var pen = new Pen(Color, Width))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                graphics.DrawLines(pen, Points.ToArray());
            }
        }
    }

    internal sealed class MosaicAnnotation : Annotation
    {
        internal Rectangle Bounds;
        internal Bitmap Pixels;
        internal override void Draw(Graphics graphics)
        {
            if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
            if (Pixels == null)
            {
                using (var brush = new HatchBrush(HatchStyle.LargeCheckerBoard,
                    Color.FromArgb(115, 31, 174, 255), Color.FromArgb(55, 8, 35, 78)))
                    graphics.FillRectangle(brush, Bounds);
                return;
            }
            var oldMode = graphics.InterpolationMode;
            var oldPixelOffset = graphics.PixelOffsetMode;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.DrawImage(Pixels, Bounds);
            graphics.InterpolationMode = oldMode;
            graphics.PixelOffsetMode = oldPixelOffset;
        }
        public override void Dispose()
        {
            if (Pixels != null) Pixels.Dispose();
        }
    }

    internal sealed class TextAnnotation : Annotation
    {
        internal Point Location;
        internal string Text;
        internal Color Color;
        internal float FontSize;
        internal override void Draw(Graphics graphics)
        {
            using (var font = new Font("Microsoft YaHei UI", FontSize, FontStyle.Bold,
                GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(Color))
                graphics.DrawString(Text, font, brush, Location);
        }
    }

    internal sealed class CaptureOverlay : Form
    {
        private readonly Bitmap snapshot;
        private readonly Rectangle virtualScreen;
        private readonly CaptureMode mode;
        private readonly ToolStrip toolbar;
        private readonly Dictionary<AnnotationTool, ToolStripButton> toolButtons =
            new Dictionary<AnnotationTool, ToolStripButton>();
        private readonly ToolStripButton undoButton;
        private readonly ToolStripButton redoButton;
        private readonly List<Annotation> annotations = new List<Annotation>();
        private readonly List<Annotation> redoAnnotations = new List<Annotation>();

        private bool selecting;
        private bool selectionReady;
        private Point selectionStart;
        private Point selectionCurrent;
        private Rectangle selection;
        private AnnotationTool activeTool;
        private Annotation workingAnnotation;
        private Point drawStart;
        private Color drawColor = Color.FromArgb(242, 55, 65);
        private float drawWidth = 4f;
        private TextBox textEditor;
        private bool closingCapture;
        private float toolbarScale = 1f;

        private ResizeHandle resizeHandle;
        private Rectangle resizeOriginal;
        private Point resizeStart;

        internal event EventHandler<CaptureImageEventArgs> CaptureCompleted;
        internal event EventHandler CaptureCanceled;
        internal event EventHandler<CaptureImageEventArgs> SaveRequested;

        internal CaptureOverlay(Bitmap snapshot, Rectangle virtualScreen, CaptureMode mode, Icon icon)
        {
            this.snapshot = snapshot;
            this.virtualScreen = virtualScreen;
            this.mode = mode;

            AutoScaleMode = AutoScaleMode.None;
            Bounds = virtualScreen;
            StartPosition = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            DoubleBuffered = true;
            Cursor = Cursors.Cross;
            Icon = icon;

            toolbar = BuildToolbar();
            toolbar.Visible = false;
            Controls.Add(toolbar);

            undoButton = FindButton("Undo");
            redoButton = FindButton("Redo");
            UpdateUndoRedo();
        }

        private ToolStrip BuildToolbar()
        {
            var strip = new ToolStrip
            {
                Dock = DockStyle.None,
                AutoSize = false,
                Size = new Size(478, 44),
                BackColor = Color.FromArgb(23, 34, 41),
                ForeColor = Color.FromArgb(234, 241, 244),
                GripStyle = ToolStripGripStyle.Hidden,
                Renderer = new CaptureToolStripRenderer(),
                Padding = new Padding(6, 4, 6, 4),
                ImageScalingSize = new Size(22, 22),
                Font = SystemFonts.MenuFont,
                ShowItemToolTips = true
            };

            AddToolButton(strip, AnnotationTool.Rectangle, "矩形框", ToolGlyph.Rectangle);
            AddToolButton(strip, AnnotationTool.Arrow, "箭头", ToolGlyph.Arrow);
            AddToolButton(strip, AnnotationTool.Pen, "画笔", ToolGlyph.Pen);
            AddToolButton(strip, AnnotationTool.Mosaic, "马赛克", ToolGlyph.Mosaic);
            AddToolButton(strip, AnnotationTool.Text, "添加文字", ToolGlyph.Text);

            strip.Items.Add(new ToolStripSeparator());

            var color = new ToolStripDropDownButton
            {
                Name = "Color",
                ToolTipText = "标注颜色",
                Image = ToolGlyphFactory.Create(ToolGlyph.Color, drawColor),
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                AutoSize = false,
                Size = new Size(34, 34),
                Tag = new Size(34, 34)
            };
            AddColorChoice(color, "红色", Color.FromArgb(242, 55, 65));
            AddColorChoice(color, "蓝色", Color.FromArgb(25, 126, 230));
            AddColorChoice(color, "绿色", Color.FromArgb(35, 181, 104));
            AddColorChoice(color, "黄色", Color.FromArgb(255, 181, 35));
            AddColorChoice(color, "白色", Color.White);
            AddColorChoice(color, "黑色", Color.Black);
            strip.Items.Add(color);

            var width = new ToolStripDropDownButton
            {
                Name = "Width",
                Text = "4 px",
                ToolTipText = "线条粗细",
                DisplayStyle = ToolStripItemDisplayStyle.Text,
                AutoSize = false,
                Size = new Size(48, 34),
                Tag = new Size(48, 34),
                ForeColor = Color.FromArgb(234, 241, 244)
            };
            AddWidthChoice(width, "细", 2f);
            AddWidthChoice(width, "中", 4f);
            AddWidthChoice(width, "粗", 7f);
            strip.Items.Add(width);
            strip.Items.Add(new ToolStripSeparator());

            var undo = CreateButton("Undo", "撤销", ToolGlyph.Undo);
            undo.Click += OnUndoClick;
            strip.Items.Add(undo);
            var redo = CreateButton("Redo", "重做", ToolGlyph.Redo);
            redo.Click += OnRedoClick;
            strip.Items.Add(redo);
            strip.Items.Add(new ToolStripSeparator());

            var save = CreateButton("Save", "保存 PNG（继续编辑）", ToolGlyph.Save);
            save.Click += OnSaveClick;
            strip.Items.Add(save);
            var cancel = CreateButton("Cancel", "取消本次截图", ToolGlyph.Cancel);
            cancel.Click += delegate { CancelCapture(); };
            strip.Items.Add(cancel);
            var done = CreateButton("Done", "完成并复制到剪贴板", ToolGlyph.Done,
                Color.FromArgb(18, 54, 77));
            done.Click += delegate { CompleteCapture(); };
            strip.Items.Add(done);
            return strip;
        }

        private void AddToolButton(ToolStrip strip, AnnotationTool tool, string tip, ToolGlyph glyph)
        {
            var button = CreateButton(tool.ToString(), tip, glyph);
            button.CheckOnClick = true;
            button.Click += delegate { SelectTool(tool); };
            strip.Items.Add(button);
            toolButtons[tool] = button;
        }

        private static ToolStripButton CreateButton(string name, string tip, ToolGlyph glyph)
        {
            return CreateButton(name, tip, glyph, Color.FromArgb(234, 241, 244));
        }

        private static ToolStripButton CreateButton(string name, string tip, ToolGlyph glyph,
            Color iconColor)
        {
            return new ToolStripButton
            {
                Name = name,
                ToolTipText = tip,
                Image = ToolGlyphFactory.Create(glyph, iconColor),
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                AutoSize = false,
                Size = new Size(34, 34),
                Tag = new Size(34, 34),
                Margin = new Padding(1, 0, 1, 0)
            };
        }

        private ToolStripButton FindButton(string name)
        {
            return toolbar.Items[name] as ToolStripButton;
        }

        private void AddColorChoice(ToolStripDropDownButton parent, string name, Color color)
        {
            var item = new ToolStripMenuItem(name)
            {
                Image = ToolGlyphFactory.Create(ToolGlyph.Color, color),
                Tag = color
            };
            item.Click += delegate(object sender, EventArgs e)
            {
                var chosen = (ToolStripMenuItem)sender;
                drawColor = (Color)chosen.Tag;
                var oldImage = parent.Image;
                parent.Image = ToolGlyphFactory.Create(ToolGlyph.Color, drawColor);
                if (oldImage != null) oldImage.Dispose();
            };
            parent.DropDownItems.Add(item);
        }

        private void AddWidthChoice(ToolStripDropDownButton parent, string name, float width)
        {
            var item = new ToolStripMenuItem(name + "  " + width.ToString("0")) { Tag = width };
            item.Click += delegate(object sender, EventArgs e)
            {
                var chosen = (ToolStripMenuItem)sender;
                drawWidth = (float)chosen.Tag;
                parent.Text = drawWidth.ToString("0") + " px";
            };
            parent.DropDownItems.Add(item);
        }

        private void SelectTool(AnnotationTool tool)
        {
            activeTool = activeTool == tool ? AnnotationTool.None : tool;
            foreach (var pair in toolButtons) pair.Value.Checked = pair.Key == activeTool;
            Cursor = activeTool == AnnotationTool.Text ? Cursors.IBeam :
                activeTool == AnnotationTool.None ? Cursors.Default : Cursors.Cross;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            Focus();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.DrawImageUnscaled(snapshot, 0, 0);
            using (var shade = new SolidBrush(Color.FromArgb(112, 0, 0, 0)))
                e.Graphics.FillRectangle(shade, ClientRectangle);

            var currentSelection = selectionReady ? selection : GetDragSelection();
            if (currentSelection.Width > 0 && currentSelection.Height > 0)
            {
                e.Graphics.DrawImage(snapshot, currentSelection, currentSelection, GraphicsUnit.Pixel);
                DrawAnnotations(e.Graphics, currentSelection.Location);
                using (var pen = new Pen(Color.FromArgb(70, 195, 223), 2f))
                    e.Graphics.DrawRectangle(pen, currentSelection.X, currentSelection.Y,
                        Math.Max(0, currentSelection.Width - 1), Math.Max(0, currentSelection.Height - 1));
                DrawSizeLabel(e.Graphics, currentSelection);
                if (selectionReady && mode == CaptureMode.Advanced && annotations.Count == 0 &&
                    workingAnnotation == null)
                    DrawResizeHandles(e.Graphics, currentSelection);
            }
            else
            {
                DrawHelp(e.Graphics);
            }
        }

        private void DrawAnnotations(Graphics graphics, Point selectionLocation)
        {
            if (!selectionReady && workingAnnotation == null) return;
            var state = graphics.Save();
            graphics.SetClip(selection);
            graphics.TranslateTransform(selectionLocation.X, selectionLocation.Y);
            foreach (var annotation in annotations) annotation.Draw(graphics);
            if (workingAnnotation != null) workingAnnotation.Draw(graphics);
            graphics.Restore(state);
        }

        private void DrawHelp(Graphics graphics)
        {
            var title = mode == CaptureMode.Advanced ? "高级截图" : "快速截图";
            const string hint = "拖动框选区域    Esc / 右键取消";
            using (var titleFont = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold))
            using (var hintFont = new Font("Microsoft YaHei UI", 9f))
            {
                var titleSize = TextRenderer.MeasureText(title, titleFont,
                    Size.Empty, TextFormatFlags.NoPadding);
                var hintSize = TextRenderer.MeasureText(hint, hintFont,
                    Size.Empty, TextFormatFlags.NoPadding);
                var contentWidth = 4 + 10 + titleSize.Width + 18 + hintSize.Width;
                var box = new Rectangle((ClientSize.Width - contentWidth) / 2 - 16,
                    24, contentWidth + 32, 38);
                using (var path = CreateRoundedRectangle(box, 10))
                using (var brush = new SolidBrush(Color.FromArgb(226, 23, 34, 41)))
                using (var border = new Pen(Color.FromArgb(170, 70, 195, 223), 1f))
                {
                    graphics.FillPath(brush, path);
                    graphics.DrawPath(border, path);
                }
                using (var accent = new SolidBrush(Color.FromArgb(255, 180, 59)))
                    graphics.FillRectangle(accent, box.Left + 14, box.Top + 10, 4, 18);
                var textY = box.Top + (box.Height - titleSize.Height) / 2;
                TextRenderer.DrawText(graphics, title, titleFont,
                    new Point(box.Left + 28, textY), Color.FromArgb(246, 249, 250),
                    TextFormatFlags.NoPadding);
                TextRenderer.DrawText(graphics, hint, hintFont,
                    new Point(box.Left + 28 + titleSize.Width + 18, textY + 1),
                    Color.FromArgb(184, 199, 205), TextFormatFlags.NoPadding);
            }
        }

        private static void DrawSizeLabel(Graphics graphics, Rectangle bounds)
        {
            var text = bounds.Width + " × " + bounds.Height;
            using (var font = new Font("Segoe UI", 9f))
            {
                var size = TextRenderer.MeasureText(text, font);
                var y = bounds.Top - size.Height - 8;
                if (y < 4) y = bounds.Top + 6;
                var box = new Rectangle(bounds.Left, y, size.Width + 12, size.Height + 4);
                using (var path = CreateRoundedRectangle(box, 5))
                using (var brush = new SolidBrush(Color.FromArgb(225, 23, 34, 41)))
                    graphics.FillPath(brush, path);
                TextRenderer.DrawText(graphics, text, font,
                    new Point(box.Left + 6, box.Top + 2), Color.FromArgb(234, 241, 244),
                    TextFormatFlags.NoPadding);
            }
        }

        private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
        {
            var diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180f, 90f);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270f, 90f);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0f, 90f);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90f, 90f);
            path.CloseFigure();
            return path;
        }

        private void DrawResizeHandles(Graphics graphics, Rectangle bounds)
        {
            foreach (var rect in GetHandleRectangles(bounds, toolbarScale).Values)
            {
                using (var brush = new SolidBrush(Color.White)) graphics.FillRectangle(brush, rect);
                using (var pen = new Pen(Color.FromArgb(22, 139, 238))) graphics.DrawRectangle(pen, rect);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right)
            {
                CancelCapture();
                return;
            }
            if (e.Button != MouseButtons.Left) return;

            if (!selectionReady)
            {
                selecting = true;
                selectionStart = ClampToClient(e.Location);
                selectionCurrent = selectionStart;
                return;
            }

            if (!selection.Contains(e.Location)) return;

            if (activeTool == AnnotationTool.Text)
            {
                BeginTextEdit(e.Location);
                return;
            }

            if (activeTool != AnnotationTool.None)
            {
                drawStart = ToRelative(e.Location);
                workingAnnotation = CreateWorkingAnnotation(activeTool, drawStart);
                Capture = true;
                Invalidate();
                return;
            }

            if (annotations.Count == 0)
            {
                resizeHandle = HitTestHandle(selection, e.Location);
                if (resizeHandle == ResizeHandle.None) resizeHandle = ResizeHandle.Move;
                resizeOriginal = selection;
                resizeStart = e.Location;
                Capture = true;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (selecting)
            {
                selectionCurrent = ClampToClient(e.Location);
                Invalidate();
                return;
            }

            if (workingAnnotation != null)
            {
                UpdateWorkingAnnotation(ToRelative(ClampToSelection(e.Location)));
                Invalidate();
                return;
            }

            if (resizeHandle != ResizeHandle.None)
            {
                UpdateSelectionResize(e.Location);
                PositionToolbar();
                Invalidate();
                return;
            }

            if (selectionReady && activeTool == AnnotationTool.None && annotations.Count == 0)
                Cursor = CursorForHandle(HitTestHandle(selection, e.Location), selection.Contains(e.Location));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;

            if (selecting)
            {
                selecting = false;
                selectionCurrent = ClampToClient(e.Location);
                selection = GetDragSelection();
                if (selection.Width < 3 || selection.Height < 3)
                {
                    selection = Rectangle.Empty;
                    Invalidate();
                    return;
                }

                selectionReady = true;
                if (mode == CaptureMode.Quick)
                    CompleteCapture();
                else
                {
                    Cursor = Cursors.Default;
                    toolbar.Visible = true;
                    PositionToolbar();
                    Invalidate();
                }
                return;
            }

            if (workingAnnotation != null)
            {
                Capture = false;
                FinalizeWorkingAnnotation();
                return;
            }

            if (resizeHandle != ResizeHandle.None)
            {
                resizeHandle = ResizeHandle.None;
                Capture = false;
                PositionToolbar();
                Invalidate();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                CancelCapture();
            }
            else if (mode == CaptureMode.Advanced && selectionReady &&
                e.Control && e.KeyCode == Keys.Z)
            {
                e.Handled = true;
                OnUndoClick(this, EventArgs.Empty);
            }
            else if (mode == CaptureMode.Advanced && selectionReady &&
                e.Control && e.KeyCode == Keys.Y)
            {
                e.Handled = true;
                OnRedoClick(this, EventArgs.Empty);
            }
            else if (mode == CaptureMode.Advanced && selectionReady && e.KeyCode == Keys.Enter &&
                textEditor == null)
            {
                e.Handled = true;
                CompleteCapture();
            }
        }

        private Rectangle GetDragSelection()
        {
            return Rectangle.FromLTRB(
                Math.Min(selectionStart.X, selectionCurrent.X),
                Math.Min(selectionStart.Y, selectionCurrent.Y),
                Math.Max(selectionStart.X, selectionCurrent.X),
                Math.Max(selectionStart.Y, selectionCurrent.Y));
        }

        private Point ClampToClient(Point point)
        {
            return new Point(Math.Max(0, Math.Min(ClientSize.Width, point.X)),
                Math.Max(0, Math.Min(ClientSize.Height, point.Y)));
        }

        private Point ClampToSelection(Point point)
        {
            return new Point(Math.Max(selection.Left, Math.Min(selection.Right - 1, point.X)),
                Math.Max(selection.Top, Math.Min(selection.Bottom - 1, point.Y)));
        }

        private Point ToRelative(Point point)
        {
            return new Point(point.X - selection.Left, point.Y - selection.Top);
        }

        private Annotation CreateWorkingAnnotation(AnnotationTool tool, Point point)
        {
            if (tool == AnnotationTool.Rectangle)
                return new RectangleAnnotation { Bounds = new Rectangle(point, Size.Empty), Color = drawColor, Width = drawWidth };
            if (tool == AnnotationTool.Arrow)
                return new ArrowAnnotation { Start = point, End = point, Color = drawColor, Width = drawWidth };
            if (tool == AnnotationTool.Pen)
            {
                var pen = new PenAnnotation { Color = drawColor, Width = drawWidth };
                pen.Points.Add(point);
                return pen;
            }
            if (tool == AnnotationTool.Mosaic)
                return new MosaicAnnotation { Bounds = new Rectangle(point, Size.Empty) };
            return null;
        }

        private void UpdateWorkingAnnotation(Point point)
        {
            var rectangle = workingAnnotation as RectangleAnnotation;
            if (rectangle != null) rectangle.Bounds = Normalize(drawStart, point);
            var arrow = workingAnnotation as ArrowAnnotation;
            if (arrow != null) arrow.End = point;
            var pen = workingAnnotation as PenAnnotation;
            if (pen != null && (pen.Points.Count == 0 || pen.Points[pen.Points.Count - 1] != point))
                pen.Points.Add(point);
            var mosaic = workingAnnotation as MosaicAnnotation;
            if (mosaic != null) mosaic.Bounds = Normalize(drawStart, point);
        }

        private void FinalizeWorkingAnnotation()
        {
            var keep = true;
            var rectangle = workingAnnotation as RectangleAnnotation;
            if (rectangle != null && (rectangle.Bounds.Width < 2 || rectangle.Bounds.Height < 2)) keep = false;
            var arrow = workingAnnotation as ArrowAnnotation;
            if (arrow != null && Distance(arrow.Start, arrow.End) < 3) keep = false;
            var pen = workingAnnotation as PenAnnotation;
            if (pen != null && pen.Points.Count < 2) keep = false;
            var mosaic = workingAnnotation as MosaicAnnotation;
            if (mosaic != null)
            {
                if (mosaic.Bounds.Width < 3 || mosaic.Bounds.Height < 3) keep = false;
                else mosaic.Pixels = BuildMosaic(mosaic.Bounds);
            }

            if (keep)
            {
                annotations.Add(workingAnnotation);
                ClearRedo();
            }
            else
            {
                workingAnnotation.Dispose();
            }
            workingAnnotation = null;
            UpdateUndoRedo();
            Invalidate();
        }

        private Bitmap BuildMosaic(Rectangle relativeBounds)
        {
            var sourceBounds = new Rectangle(selection.Left + relativeBounds.Left,
                selection.Top + relativeBounds.Top, relativeBounds.Width, relativeBounds.Height);
            var block = Math.Max(3, (int)Math.Round(drawWidth * 0.75f));
            var smallWidth = Math.Max(1, relativeBounds.Width / block);
            var smallHeight = Math.Max(1, relativeBounds.Height / block);
            using (var crop = new Bitmap(relativeBounds.Width, relativeBounds.Height, PixelFormat.Format32bppArgb))
            {
                using (var graphics = Graphics.FromImage(crop))
                    graphics.DrawImage(snapshot, new Rectangle(Point.Empty, crop.Size), sourceBounds, GraphicsUnit.Pixel);
                var pixels = new Bitmap(smallWidth, smallHeight, PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(pixels))
                {
                    graphics.InterpolationMode = InterpolationMode.Low;
                    graphics.DrawImage(crop, new Rectangle(Point.Empty, pixels.Size));
                }
                return pixels;
            }
        }

        private static Rectangle Normalize(Point first, Point second)
        {
            return Rectangle.FromLTRB(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y),
                Math.Max(first.X, second.X), Math.Max(first.Y, second.Y));
        }

        private static double Distance(Point first, Point second)
        {
            var x = first.X - second.X;
            var y = first.Y - second.Y;
            return Math.Sqrt(x * x + y * y);
        }

        private void BeginTextEdit(Point absolutePoint)
        {
            CommitTextEdit();
            var availableWidth = selection.Right - absolutePoint.X;
            var width = Math.Max(1, Math.Min(availableWidth,
                (int)Math.Round(320 * toolbarScale)));
            width = Math.Max(Math.Min(availableWidth,
                (int)Math.Round(90 * toolbarScale)), width);
            textEditor = new TextBox
            {
                Location = absolutePoint,
                Width = width,
                Height = Math.Max(24, (int)Math.Round(32 * toolbarScale)),
                Font = new Font("Microsoft YaHei UI", 18f * toolbarScale, FontStyle.Bold,
                    GraphicsUnit.Pixel),
                ForeColor = drawColor,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            textEditor.KeyDown += OnTextEditorKeyDown;
            textEditor.LostFocus += OnTextEditorLostFocus;
            Controls.Add(textEditor);
            textEditor.BringToFront();
            textEditor.Focus();
        }

        private void OnTextEditorKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                CancelCapture();
            }
            else if (e.Control && e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                CommitTextEdit();
            }
        }

        private void OnTextEditorLostFocus(object sender, EventArgs e)
        {
            if (textEditor != null && !toolbar.ContainsFocus) CommitTextEdit();
        }

        private void CommitTextEdit()
        {
            if (textEditor == null) return;
            var editor = textEditor;
            textEditor = null;
            editor.KeyDown -= OnTextEditorKeyDown;
            editor.LostFocus -= OnTextEditorLostFocus;
            var text = editor.Text.Trim();
            var location = new Point(editor.Left - selection.Left, editor.Top - selection.Top);
            Controls.Remove(editor);
            editor.Dispose();
            if (text.Length > 0)
            {
                annotations.Add(new TextAnnotation
                {
                    Location = location,
                    Text = text,
                    Color = editor.ForeColor,
                    FontSize = 18f * toolbarScale
                });
                ClearRedo();
                UpdateUndoRedo();
                Invalidate();
            }
        }

        private void CancelTextEdit()
        {
            if (textEditor == null) return;
            var editor = textEditor;
            textEditor = null;
            editor.KeyDown -= OnTextEditorKeyDown;
            editor.LostFocus -= OnTextEditorLostFocus;
            Controls.Remove(editor);
            editor.Dispose();
            Focus();
        }

        private void OnUndoClick(object sender, EventArgs e)
        {
            CommitTextEdit();
            if (annotations.Count == 0) return;
            var index = annotations.Count - 1;
            redoAnnotations.Add(annotations[index]);
            annotations.RemoveAt(index);
            UpdateUndoRedo();
            Invalidate();
        }

        private void OnRedoClick(object sender, EventArgs e)
        {
            if (redoAnnotations.Count == 0) return;
            var index = redoAnnotations.Count - 1;
            annotations.Add(redoAnnotations[index]);
            redoAnnotations.RemoveAt(index);
            UpdateUndoRedo();
            Invalidate();
        }

        private void ClearRedo()
        {
            foreach (var annotation in redoAnnotations) annotation.Dispose();
            redoAnnotations.Clear();
        }

        private void UpdateUndoRedo()
        {
            if (undoButton != null) undoButton.Enabled = annotations.Count > 0;
            if (redoButton != null) redoButton.Enabled = redoAnnotations.Count > 0;
        }

        private void OnSaveClick(object sender, EventArgs e)
        {
            CommitTextEdit();
            using (var image = RenderSelection(true))
            {
                var handler = SaveRequested;
                if (handler != null)
                {
                    var args = new CaptureImageEventArgs(image);
                    handler(this, args);
                }
            }
        }

        private void UpdateCursorForActiveTool()
        {
            Cursor = activeTool == AnnotationTool.Text ? Cursors.IBeam :
                activeTool == AnnotationTool.None ? Cursors.Default : Cursors.Cross;
        }

        private void PositionToolbar()
        {
            if (!toolbar.Visible || selection.IsEmpty) return;
            ApplyToolbarScale();
            var x = selection.Left;
            var y = selection.Bottom + 8;
            var currentScreen = Screen.FromPoint(PointToScreen(selection.Location));
            var area = RectangleToClient(currentScreen.WorkingArea);
            if (x + toolbar.Width > area.Right) x = area.Right - toolbar.Width;
            if (x < area.Left) x = area.Left;
            if (y + toolbar.Height > area.Bottom) y = selection.Top - toolbar.Height - 8;
            if (y < area.Top) y = Math.Max(area.Top, selection.Bottom - toolbar.Height - 6);
            toolbar.Location = new Point(x, y);
            toolbar.BringToFront();
        }

        private void ApplyToolbarScale()
        {
            var center = new Point(selection.Left + selection.Width / 2,
                selection.Top + selection.Height / 2);
            var dpi = NativeMethods.GetDpiForPoint(PointToScreen(center));
            var scale = Math.Max(1f, dpi / 96f);
            if (Math.Abs(scale - toolbarScale) < 0.05f) return;

            toolbarScale = scale;
            toolbar.SuspendLayout();
            toolbar.Size = new Size((int)Math.Round(520 * scale),
                (int)Math.Round(44 * scale));
            toolbar.Padding = new Padding((int)Math.Round(6 * scale),
                (int)Math.Round(4 * scale), (int)Math.Round(6 * scale),
                (int)Math.Round(4 * scale));
            toolbar.ImageScalingSize = new Size((int)Math.Round(22 * scale),
                (int)Math.Round(22 * scale));
            foreach (ToolStripItem item in toolbar.Items)
            {
                if (item is ToolStripButton || item is ToolStripDropDownButton)
                {
                    var baseSize = item.Tag is Size ? (Size)item.Tag : new Size(34, 34);
                    item.AutoSize = false;
                    item.Size = new Size((int)Math.Round(baseSize.Width * scale),
                        (int)Math.Round(baseSize.Height * scale));
                    item.Margin = new Padding(Math.Max(1, (int)Math.Round(scale)),
                        0, Math.Max(1, (int)Math.Round(scale)), 0);
                }
            }
            toolbar.ResumeLayout();
        }

        private Bitmap RenderSelection(bool includeAnnotations)
        {
            var result = new Bitmap(selection.Width, selection.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(result))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.DrawImage(snapshot, new Rectangle(Point.Empty, result.Size), selection, GraphicsUnit.Pixel);
                if (includeAnnotations)
                    foreach (var annotation in annotations) annotation.Draw(graphics);
            }
            return result;
        }

        private void CompleteCapture()
        {
            if (closingCapture) return;
            CommitTextEdit();
            if (!selectionReady || selection.Width < 3 || selection.Height < 3) return;
            var image = RenderSelection(mode == CaptureMode.Advanced);
            closingCapture = true;
            Hide();
            try
            {
                var handler = CaptureCompleted;
                if (handler != null) handler(this, new CaptureImageEventArgs(image));
                else image.Dispose();
            }
            finally
            {
                Close();
            }
        }

        private void CancelCapture()
        {
            if (closingCapture) return;
            closingCapture = true;
            Hide();
            var handler = CaptureCanceled;
            if (handler != null) handler(this, EventArgs.Empty);
            Close();
        }

        private void UpdateSelectionResize(Point point)
        {
            var dx = point.X - resizeStart.X;
            var dy = point.Y - resizeStart.Y;
            var rect = resizeOriginal;
            if (resizeHandle == ResizeHandle.Move)
            {
                rect.Offset(dx, dy);
                if (rect.Left < 0) rect.X = 0;
                if (rect.Top < 0) rect.Y = 0;
                if (rect.Right > ClientSize.Width) rect.X = ClientSize.Width - rect.Width;
                if (rect.Bottom > ClientSize.Height) rect.Y = ClientSize.Height - rect.Height;
            }
            else
            {
                var left = rect.Left;
                var top = rect.Top;
                var right = rect.Right;
                var bottom = rect.Bottom;
                if (HasLeft(resizeHandle)) left += dx;
                if (HasRight(resizeHandle)) right += dx;
                if (HasTop(resizeHandle)) top += dy;
                if (HasBottom(resizeHandle)) bottom += dy;
                left = Math.Max(0, Math.Min(left, right - 10));
                top = Math.Max(0, Math.Min(top, bottom - 10));
                right = Math.Min(ClientSize.Width, Math.Max(right, left + 10));
                bottom = Math.Min(ClientSize.Height, Math.Max(bottom, top + 10));
                rect = Rectangle.FromLTRB(left, top, right, bottom);
            }
            selection = rect;
        }

        private static Dictionary<ResizeHandle, Rectangle> GetHandleRectangles(
            Rectangle bounds, float scale)
        {
            var size = Math.Max(8, (int)Math.Round(8 * scale));
            var half = size / 2;
            var centerX = bounds.Left + bounds.Width / 2;
            var centerY = bounds.Top + bounds.Height / 2;
            return new Dictionary<ResizeHandle, Rectangle>
            {
                { ResizeHandle.NorthWest, new Rectangle(bounds.Left - half, bounds.Top - half, size, size) },
                { ResizeHandle.North, new Rectangle(centerX - half, bounds.Top - half, size, size) },
                { ResizeHandle.NorthEast, new Rectangle(bounds.Right - half, bounds.Top - half, size, size) },
                { ResizeHandle.East, new Rectangle(bounds.Right - half, centerY - half, size, size) },
                { ResizeHandle.SouthEast, new Rectangle(bounds.Right - half, bounds.Bottom - half, size, size) },
                { ResizeHandle.South, new Rectangle(centerX - half, bounds.Bottom - half, size, size) },
                { ResizeHandle.SouthWest, new Rectangle(bounds.Left - half, bounds.Bottom - half, size, size) },
                { ResizeHandle.West, new Rectangle(bounds.Left - half, centerY - half, size, size) }
            };
        }

        private ResizeHandle HitTestHandle(Rectangle bounds, Point point)
        {
            foreach (var pair in GetHandleRectangles(bounds, toolbarScale))
                if (pair.Value.Contains(point)) return pair.Key;
            return ResizeHandle.None;
        }

        private static Cursor CursorForHandle(ResizeHandle handle, bool inside)
        {
            if (handle == ResizeHandle.North || handle == ResizeHandle.South) return Cursors.SizeNS;
            if (handle == ResizeHandle.East || handle == ResizeHandle.West) return Cursors.SizeWE;
            if (handle == ResizeHandle.NorthWest || handle == ResizeHandle.SouthEast) return Cursors.SizeNWSE;
            if (handle == ResizeHandle.NorthEast || handle == ResizeHandle.SouthWest) return Cursors.SizeNESW;
            return inside ? Cursors.SizeAll : Cursors.Default;
        }

        private static bool HasLeft(ResizeHandle value)
        {
            return value == ResizeHandle.West || value == ResizeHandle.NorthWest || value == ResizeHandle.SouthWest;
        }
        private static bool HasRight(ResizeHandle value)
        {
            return value == ResizeHandle.East || value == ResizeHandle.NorthEast || value == ResizeHandle.SouthEast;
        }
        private static bool HasTop(ResizeHandle value)
        {
            return value == ResizeHandle.North || value == ResizeHandle.NorthWest || value == ResizeHandle.NorthEast;
        }
        private static bool HasBottom(ResizeHandle value)
        {
            return value == ResizeHandle.South || value == ResizeHandle.SouthWest || value == ResizeHandle.SouthEast;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                closingCapture = true;
                if (textEditor != null) textEditor.Dispose();
                if (workingAnnotation != null) workingAnnotation.Dispose();
                foreach (var annotation in annotations) annotation.Dispose();
                foreach (var annotation in redoAnnotations) annotation.Dispose();
                snapshot.Dispose();
            }
            base.Dispose(disposing);
        }

        private enum ResizeHandle
        {
            None, Move, North, South, East, West, NorthWest, NorthEast, SouthWest, SouthEast
        }
    }

    #if OCR_ENABLED
    internal sealed class OcrResultForm : Form
    {
        private readonly TextBox resultBox;
        private readonly Label status;
        internal event EventHandler TextCopied;

        internal OcrResultForm(string text, Icon icon)
        {
            Text = "快截 - 提取文字";
            Icon = icon;
            ShowInTaskbar = false;
            TopMost = true;
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            MinimumSize = new Size(330, 260);
            Size = new Size(410, 430);
            Font = new Font("Microsoft YaHei UI", 9f);
            BackColor = Color.FromArgb(245, 248, 252);

            resultBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Both,
                WordWrap = true,
                Text = text ?? string.Empty,
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White,
                Font = new Font("Microsoft YaHei UI", 10f),
                Margin = new Padding(10)
            };

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 70, Padding = new Padding(10, 7, 10, 7) };
            status = new Label
            {
                Text = string.IsNullOrWhiteSpace(text) ? "没有识别到文字" : "可选择部分文字，或复制全部",
                AutoSize = true,
                ForeColor = Color.FromArgb(80, 88, 100),
                Location = new Point(10, 9)
            };
            bottom.Controls.Add(status);

            var copySelected = new Button
            {
                Text = "复制所选",
                Size = new Size(86, 28),
                Location = new Point(10, 34)
            };
            copySelected.Click += delegate { CopyText(resultBox.SelectedText); };
            bottom.Controls.Add(copySelected);

            var copyAll = new Button
            {
                Text = "复制全部",
                Size = new Size(86, 28),
                Location = new Point(102, 34),
                BackColor = Color.FromArgb(25, 112, 226),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            copyAll.FlatAppearance.BorderSize = 0;
            copyAll.Click += delegate { CopyText(resultBox.Text); };
            bottom.Controls.Add(copyAll);

            var close = new Button
            {
                Text = "关闭",
                Size = new Size(72, 28),
                Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
                Location = new Point(ClientSize.Width - 82, 34)
            };
            close.Click += delegate { Close(); };
            bottom.Controls.Add(close);
            bottom.Resize += delegate { close.Left = bottom.ClientSize.Width - close.Width - 10; };

            Controls.Add(resultBox);
            Controls.Add(bottom);
        }

        private void CopyText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                status.Text = "请先选择文字，或使用“复制全部”";
                return;
            }
            try
            {
                ClipboardHelper.SetText(text);
                status.Text = "文字已复制";
                var handler = TextCopied;
                if (handler != null) handler(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "复制文字失败：" + ex.Message, ProductInfo.DisplayName,
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
    #endif

    internal enum ToolGlyph
    {
        Rectangle, Arrow, Pen, Mosaic, Text, Color, Undo, Redo, Save, Cancel, Done
    }

    internal static class ToolGlyphFactory
    {
        internal static Bitmap Create(ToolGlyph glyph, Color color)
        {
            var bitmap = new Bitmap(24, 24, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            using (var pen = new Pen(color, 2f))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                if (glyph == ToolGlyph.Rectangle) graphics.DrawRectangle(pen, 4, 5, 16, 14);
                else if (glyph == ToolGlyph.Arrow)
                {
                    graphics.DrawLine(pen, 4, 18, 19, 6);
                    graphics.DrawLine(pen, 13, 6, 19, 6);
                    graphics.DrawLine(pen, 19, 6, 19, 12);
                }
                else if (glyph == ToolGlyph.Pen)
                {
                    graphics.DrawLine(pen, 5, 18, 17, 6);
                    graphics.DrawLine(pen, 17, 6, 20, 9);
                    graphics.DrawLine(pen, 20, 9, 8, 21);
                    graphics.DrawLine(pen, 5, 18, 8, 21);
                    graphics.DrawLine(pen, 5, 18, 4, 22);
                }
                else if (glyph == ToolGlyph.Mosaic)
                {
                    using (var brush = new SolidBrush(color))
                        for (var y = 4; y <= 16; y += 6)
                            for (var x = 4; x <= 16; x += 6)
                                graphics.FillRectangle(brush, x, y, 4, 4);
                }
                else if (glyph == ToolGlyph.Text)
                {
                    using (var font = new Font("Segoe UI", 14f, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var brush = new SolidBrush(color))
                        graphics.DrawString("T", font, brush, 7f, 3f);
                }
                else if (glyph == ToolGlyph.Color)
                {
                    using (var brush = new SolidBrush(color)) graphics.FillEllipse(brush, 5, 5, 14, 14);
                    using (var outline = new Pen(Color.FromArgb(140, Color.White), 1f))
                        graphics.DrawEllipse(outline, 5, 5, 14, 14);
                }
                else if (glyph == ToolGlyph.Undo || glyph == ToolGlyph.Redo)
                {
                    if (glyph == ToolGlyph.Undo)
                    {
                        graphics.DrawArc(pen, 5, 5, 14, 14, 205, 250);
                        graphics.DrawLine(pen, 4, 9, 4, 4); graphics.DrawLine(pen, 4, 4, 9, 4);
                    }
                    else
                    {
                        graphics.DrawArc(pen, 5, 5, 14, 14, 85, 250);
                        graphics.DrawLine(pen, 20, 9, 20, 4); graphics.DrawLine(pen, 20, 4, 15, 4);
                    }
                }
                else if (glyph == ToolGlyph.Save)
                {
                    graphics.DrawLine(pen, 12, 3, 12, 15);
                    graphics.DrawLine(pen, 7, 10, 12, 15);
                    graphics.DrawLine(pen, 17, 10, 12, 15);
                    graphics.DrawLine(pen, 5, 18, 5, 21);
                    graphics.DrawLine(pen, 5, 21, 19, 21);
                    graphics.DrawLine(pen, 19, 21, 19, 18);
                }
                else if (glyph == ToolGlyph.Cancel)
                {
                    graphics.DrawLine(pen, 5, 5, 19, 19); graphics.DrawLine(pen, 19, 5, 5, 19);
                }
                else if (glyph == ToolGlyph.Done)
                {
                    graphics.DrawLine(pen, 4, 12, 9, 18); graphics.DrawLine(pen, 9, 18, 21, 5);
                }
            }
            return bitmap;
        }
    }

    internal sealed class CaptureToolStripRenderer : ToolStripProfessionalRenderer
    {
        internal CaptureToolStripRenderer() : base(new CaptureColorTable()) { RoundedEdges = true; }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip is ToolStripDropDownMenu)
            {
                base.OnRenderToolStripBackground(e);
                return;
            }
            var bounds = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            using (var path = RoundedRectangle(bounds, 10))
            using (var brush = new SolidBrush(Color.FromArgb(23, 34, 41)))
            using (var border = new Pen(Color.FromArgb(58, 76, 84)))
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(border, path);
            }
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            if (e.ToolStrip is ToolStripDropDownMenu) base.OnRenderToolStripBorder(e);
        }

        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            DrawCaptureButtonBackground(e);
        }

        protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e)
        {
            DrawCaptureButtonBackground(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            if (!e.Vertical)
            {
                base.OnRenderSeparator(e);
                return;
            }
            var x = e.Item.Width / 2;
            using (var pen = new Pen(Color.FromArgb(65, 83, 91)))
                e.Graphics.DrawLine(pen, x, 8, x, Math.Max(8, e.Item.Height - 8));
        }

        private static void DrawCaptureButtonBackground(ToolStripItemRenderEventArgs e)
        {
            var button = e.Item as ToolStripButton;
            var isDone = string.Equals(e.Item.Name, "Done", StringComparison.Ordinal);
            var isChecked = button != null && button.Checked;
            if (!isDone && !isChecked && !e.Item.Selected && !e.Item.Pressed) return;

            Color fill;
            Color borderColor;
            if (isDone)
            {
                fill = e.Item.Pressed ? Color.FromArgb(232, 151, 31) :
                    e.Item.Selected ? Color.FromArgb(255, 194, 86) : Color.FromArgb(255, 180, 59);
                borderColor = Color.FromArgb(255, 202, 112);
            }
            else if (isChecked)
            {
                fill = Color.FromArgb(35, 69, 78);
                borderColor = Color.FromArgb(70, 195, 223);
            }
            else
            {
                fill = e.Item.Pressed ? Color.FromArgb(53, 69, 77) : Color.FromArgb(42, 56, 63);
                borderColor = Color.FromArgb(69, 89, 98);
            }

            var bounds = new Rectangle(1, 2, Math.Max(1, e.Item.Width - 3),
                Math.Max(1, e.Item.Height - 4));
            using (var path = RoundedRectangle(bounds, 7))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(borderColor))
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.FillPath(brush, path);
                e.Graphics.DrawPath(pen, path);
            }
        }

        private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
        {
            var diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180f, 90f);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270f, 90f);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0f, 90f);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90f, 90f);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class CaptureColorTable : ProfessionalColorTable
    {
        public override Color ToolStripGradientBegin { get { return Color.FromArgb(23, 34, 41); } }
        public override Color ToolStripGradientMiddle { get { return Color.FromArgb(23, 34, 41); } }
        public override Color ToolStripGradientEnd { get { return Color.FromArgb(23, 34, 41); } }
        public override Color ButtonSelectedHighlight { get { return Color.FromArgb(42, 56, 63); } }
        public override Color ButtonSelectedGradientBegin { get { return Color.FromArgb(42, 56, 63); } }
        public override Color ButtonSelectedGradientEnd { get { return Color.FromArgb(42, 56, 63); } }
        public override Color ButtonPressedGradientBegin { get { return Color.FromArgb(53, 69, 77); } }
        public override Color ButtonPressedGradientEnd { get { return Color.FromArgb(53, 69, 77); } }
        public override Color MenuItemBorder { get { return Color.FromArgb(70, 195, 223); } }
        public override Color MenuItemSelected { get { return Color.FromArgb(222, 242, 245); } }
        public override Color MenuItemSelectedGradientBegin { get { return Color.FromArgb(222, 242, 245); } }
        public override Color MenuItemSelectedGradientEnd { get { return Color.FromArgb(222, 242, 245); } }
    }
}
