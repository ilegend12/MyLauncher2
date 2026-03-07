using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net.Http;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace MyLauncher2
{
    public partial class FrmMain : Form
    {
        #region Notice panel

        private class NoticeTopic
        {
            public string type { get; set; }
            public string name { get; set; }
            public string link { get; set; }
            public string date { get; set; }
        }

        private class NoticeData
        {
            public List<NoticeTopic> topics { get; set; }
        }

        private List<NoticeTopic> _noticeTopics = new List<NoticeTopic>();
        private Panel _noticePanelControl;
        private int _noticeHoverRow = -1;

        private const int NoticeRowH = 28;
        private const int NoticePadY = 4;
        private const int BadgeW = 58;
        private const int BadgeX = 8;
        private const int DateW = 68;

        // Scroll state
        private int _noticeScrollOffset = 0;  // top row index (for scrolling when > 10 items)

        // Marquee state
        private int _marqueeOffset = 0;          // current pixel offset of scrolling text
        private System.Windows.Forms.Timer _marqueeTimer = new System.Windows.Forms.Timer(); // fires every ~30ms
        private int _marqueeRow = -1;             // which row is being marquee'd
        private float _marqueeTextWidth = 0f;     // full text pixel width
        private float _marqueeColumnWidth = 0f;   // available column width

        private void BuildNoticePanel()
        {
            var pnl = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent
            };

            pnl.Paint += NoticePanel_Paint;
            pnl.MouseMove += NoticePanel_MouseMove;
            pnl.MouseLeave += NoticePanel_MouseLeave;
            pnl.MouseClick += NoticePanel_MouseClick;
            pnl.MouseWheel += NoticePanel_MouseWheel;

            _marqueeTimer.Interval = 30;
            _marqueeTimer.Tick += MarqueeTimer_Tick;

            _noticePanelControl = pnl;
            noticePanel.Controls.Add(pnl);
        }

        private void LoadNoticeJson()
        {
            try
            {
                string path = Path.Combine(Application.StartupPath, "notice.json");
                if (!File.Exists(path)) return;

                string json = File.ReadAllText(path);
                var data = JsonConvert.DeserializeObject<NoticeData>(json);
                if (data?.topics == null) return;

                _noticeTopics.Clear();
                _noticeTopics.AddRange(data.topics);

                // Sort by date descending (latest first)
                _noticeTopics.Sort((a, b) =>
                {
                    DateTime da, db;
                    bool pa = DateTime.TryParse(a.date ?? "", out da);
                    bool pb = DateTime.TryParse(b.date ?? "", out db);
                    if (pa && pb) return db.CompareTo(da);
                    if (pa) return -1;
                    if (pb) return 1;
                    return 0;
                });
                _noticeScrollOffset = 0;

                _noticePanelControl?.Invalidate();
            }
            catch { }
        }

        private void NoticePanel_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            if (_noticeTopics.Count == 0)
            {
                using (var f = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point))
                using (var b = new SolidBrush(Color.FromArgb(120, 120, 130)))
                {
                    var r = new RectangleF(0, 0, _noticePanelControl.Width, _noticePanelControl.Height);
                    using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString("No notices", f, b, r, sf);
                }
                return;
            }

            int visibleCount = Math.Min(10, _noticeTopics.Count - _noticeScrollOffset);

            for (int i = _noticeScrollOffset; i < _noticeScrollOffset + visibleCount; i++)
            {
                int displayRow = i - _noticeScrollOffset;
                var topic = _noticeTopics[i];
                float y = NoticePadY + displayRow * NoticeRowH;
                bool hover = (i == _noticeHoverRow);

                // Row background
                var rowRect = new RectangleF(0, y, _noticePanelControl.Width, NoticeRowH);
                if (hover)
                {
                    using (var hb = new SolidBrush(Color.FromArgb(60, 120, 180, 255)))
                        g.FillRectangle(hb, rowRect);
                }

                // Badge
                var badgeRect = new RectangleF(BadgeX, y + 4, BadgeW, NoticeRowH - 8);
                Color badgeColor = GetBadgeColor(topic.type);
                using (var bb = new SolidBrush(badgeColor))
                    g.FillRectangle(bb, badgeRect);

                using (var bf = new Font("Segoe UI", 7.5f, FontStyle.Bold, GraphicsUnit.Point))
                using (var bsf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                using (var wBrush = new SolidBrush(Color.White))
                    g.DrawString((topic.type ?? "").ToUpper(), bf, wBrush, badgeRect, bsf);

                // Name column
                int nameX = BadgeX + BadgeW + 8;
                float nameW = _noticePanelControl.Width - nameX - DateW - 10;
                var nameRect = new RectangleF(nameX, y + 1, nameW, NoticeRowH - 2);
                var nameStyle = hover ? FontStyle.Bold : FontStyle.Regular;

                bool isMarqueeRow = (i == _marqueeRow && _marqueeOffset > 0);

                using (var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center })
                using (var nameFont = new Font("Segoe UI", 8.5f, nameStyle, GraphicsUnit.Point))
                using (var b = new SolidBrush(hover ? Color.White : Color.FromArgb(210, 210, 220)))
                {
                    if (isMarqueeRow)
                    {
                        g.SetClip(nameRect);
                        var shiftedRect = new RectangleF(nameRect.X - _marqueeOffset, nameRect.Y, nameRect.Width + _marqueeOffset + _marqueeTextWidth, nameRect.Height);
                        g.DrawString(topic.name ?? "", nameFont, b, shiftedRect, sf);
                        g.ResetClip();
                    }
                    else
                    {
                        sf.Trimming = StringTrimming.EllipsisCharacter;
                        sf.FormatFlags = StringFormatFlags.NoWrap;
                        g.DrawString(topic.name ?? "", nameFont, b, nameRect, sf);
                    }
                }

                // Date column
                if (!string.IsNullOrEmpty(topic.date))
                {
                    var dateRect = new RectangleF(_noticePanelControl.Width - DateW - 4, y + 1, DateW, NoticeRowH - 2);
                    using (var df = new Font("Segoe UI", 7.5f, FontStyle.Regular, GraphicsUnit.Point))
                    using (var db = new SolidBrush(hover ? Color.FromArgb(200, 235, 255) : Color.FromArgb(140, 140, 155)))
                    using (var dsf = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center })
                        g.DrawString(topic.date, df, db, dateRect, dsf);
                }

                // Separator
                if (displayRow < visibleCount - 1)
                {
                    using (var pen = new Pen(Color.FromArgb(40, 255, 255, 255)))
                        g.DrawLine(pen, BadgeX, y + NoticeRowH - 1, _noticePanelControl.Width - BadgeX, y + NoticeRowH - 1);
                }
            }
        }

        private void NoticePanel_MouseMove(object sender, MouseEventArgs e)
        {
            int displayRow = GetNoticeRowAt(e.Y);
            int row = displayRow >= 0 ? displayRow + _noticeScrollOffset : -1;
            if (row == _noticeHoverRow) return;

            // Stop old marquee
            _marqueeTimer.Stop();
            _marqueeOffset = 0;
            _marqueeRow = -1;

            _noticeHoverRow = row;

            if (row >= 0 && row < _noticeTopics.Count)
            {
                bool hasLink = HasRealLink(_noticeTopics[row].link);
                _noticePanelControl.Cursor = hasLink ? Cursors.Hand : Cursors.Default;

                // Measure text to decide if marquee is needed
                using (var g = _noticePanelControl.CreateGraphics())
                using (var font = new Font("Segoe UI", 8.5f, FontStyle.Bold, GraphicsUnit.Point))
                {
                    int nameX = BadgeX + BadgeW + 8;
                    float colW = _noticePanelControl.Width - nameX - DateW - 10;
                    float textW = g.MeasureString(_noticeTopics[row].name ?? "", font).Width;
                    if (textW > colW)
                    {
                        _marqueeRow = row;
                        _marqueeTextWidth = textW + 20; // 20px gap before repeat
                        _marqueeColumnWidth = colW;
                        _marqueeTimer.Start();
                    }
                }
            }
            else
            {
                _noticePanelControl.Cursor = Cursors.Default;
            }

            _noticePanelControl.Invalidate();
        }

        private void NoticePanel_MouseLeave(object sender, EventArgs e)
        {
            _marqueeTimer.Stop();
            _marqueeOffset = 0;
            _marqueeRow = -1;
            _noticeHoverRow = -1;
            _noticePanelControl.Cursor = Cursors.Default;
            _noticePanelControl.Invalidate();
        }

        private void NoticePanel_MouseClick(object sender, MouseEventArgs e)
        {
            int displayRow = GetNoticeRowAt(e.Y);
            int row = displayRow >= 0 ? displayRow + _noticeScrollOffset : -1;
            if (row >= 0 && row < _noticeTopics.Count)
            {
                string link = _noticeTopics[row].link;
                if (HasRealLink(link))
                {
                    try { System.Diagnostics.Process.Start(link); } catch { }
                }
            }
        }

        private void NoticePanel_MouseWheel(object sender, MouseEventArgs e)
        {
            int maxScroll = Math.Max(0, _noticeTopics.Count - 10);
            _noticeScrollOffset -= e.Delta > 0 ? 1 : -1;
            _noticeScrollOffset = Math.Max(0, Math.Min(_noticeScrollOffset, maxScroll));
            _noticePanelControl.Invalidate();
        }

        private void MarqueeTimer_Tick(object sender, EventArgs e)
        {
            _marqueeOffset += 1; // 1px per tick (slow scroll at ~30fps = ~30px/sec)
            if (_marqueeOffset >= _marqueeTextWidth)
                _marqueeOffset = 0; // loop back to start
            _noticePanelControl.Invalidate();
        }

        private int GetNoticeRowAt(int mouseY)
        {
            if (mouseY < NoticePadY) return -1;
            int row = (mouseY - NoticePadY) / NoticeRowH;
            int visibleCount = Math.Min(10, _noticeTopics.Count - _noticeScrollOffset);
            if (row >= visibleCount) return -1;
            return row;
        }

        private static bool HasRealLink(string link)
        {
            return !string.IsNullOrWhiteSpace(link)
                && (link.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                    || link.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        }

        private static Color GetBadgeColor(string type)
        {
            switch ((type ?? "").ToLowerInvariant())
            {
                case "notice": return Color.FromArgb(70, 130, 180);
                case "event":  return Color.FromArgb(180, 100, 50);
                case "update": return Color.FromArgb(60, 150, 80);
                case "patch":  return Color.FromArgb(130, 60, 160);
                default:       return Color.FromArgb(100, 100, 110);
            }
        }

        #endregion
    }
}
