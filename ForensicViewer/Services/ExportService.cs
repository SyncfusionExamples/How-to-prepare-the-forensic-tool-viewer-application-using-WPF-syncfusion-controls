using System;
using System.Collections.Generic;
using ForensicViewer.Models;
using ForensicViewer.ViewModels;
using Syncfusion.XlsIO;
using PointF = System.Drawing.PointF;
using RectangleF = System.Drawing.RectangleF;

namespace ForensicViewer.Services
{
    /// <summary>Excel / PDF report generation (FR-13). No UI dependencies.</summary>
    public static class ExportService
    {
        public static string FilterSummary(MainViewModel vm) =>
            $"Type={vm.SelectedEvidenceType}; App={vm.SelectedApplication}; Device={vm.SelectedDevice}; " +
            $"Search='{vm.FreeTextSearch}'; " +
            $"Timeline={(vm.TimelineStart?.ToString("yyyy-MM-dd") ?? "all")} to {(vm.TimelineEnd?.ToString("yyyy-MM-dd") ?? "all")}; " +
            $"ChartDrill={(vm.DrillValue ?? "none")}";

        public static void ExportExcel(string path, MainViewModel vm)
        {
            var records = vm.FilteredRecords;
            using var engine = new ExcelEngine();
            var workbook = engine.Excel.Workbooks.Create(1);
            var sheet = workbook.Worksheets[0];

            sheet.Range["A1"].Text = "Forensic Tool Viewer - Evidence Export";
            sheet.Range["A2"].Text = "Case ID: CASE-2026-001";
            sheet.Range["A3"].Text = $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss} (local)";
            sheet.Range["A4"].Text = "Filters: " + FilterSummary(vm);
            sheet.Range["A5"].Text = $"Record count: {records.Count:N0}";
            sheet.Range["A1"].CellStyle.Font.Bold = true;

            int headerRow = 7;
            string[] headers = { "Timestamp", "Type", "Application", "Device", "Direction",
                                 "Counterparty", "Content", "Status", "SizeBytes", "MD5", "SHA-256" };
            for (int i = 0; i < headers.Length; i++)
            {
                sheet.Range[headerRow, i + 1].Text = headers[i];
                sheet.Range[headerRow, i + 1].CellStyle.Font.Bold = true;
            }

            int row = headerRow + 1;
            foreach (var r in records)
            {
                sheet.Range[row, 1].DateTime = r.Timestamp;
                sheet.Range[row, 2].Text = r.EvidenceType.ToString();
                sheet.Range[row, 3].Text = r.Application;
                sheet.Range[row, 4].Text = r.DeviceName;
                sheet.Range[row, 5].Text = r.Direction.ToString();
                sheet.Range[row, 6].Text = r.Counterparty;
                sheet.Range[row, 7].Text = r.Content;
                sheet.Range[row, 8].Text = r.RecoveryStatus.ToString();
                sheet.Range[row, 9].Number = r.SizeBytes;
                sheet.Range[row, 10].Text = r.HashMD5;
                sheet.Range[row, 11].Text = r.HashSHA256;
                row++;
            }

            sheet.UsedRange.AutofitColumns();
            workbook.SaveAs(path);
        }

        public static void ExportPdf(string path, MainViewModel vm, IReadOnlyList<ChartSnapshot> snapshots)
        {
            using var doc = new Syncfusion.Pdf.PdfDocument();
            var page = doc.Pages.Add();
            var g = page.Graphics;
            float width = page.GetClientSize().Width;
            float y = 0;

            var titleFont = new Syncfusion.Pdf.Graphics.PdfTrueTypeFont(
                new System.Drawing.Font("Segoe UI", 20, System.Drawing.FontStyle.Bold), true);
            var hFont = new Syncfusion.Pdf.Graphics.PdfTrueTypeFont(
                new System.Drawing.Font("Segoe UI", 12, System.Drawing.FontStyle.Bold), true);
            var bodyFont = new Syncfusion.Pdf.Graphics.PdfTrueTypeFont(
                new System.Drawing.Font("Segoe UI", 10), true);
            var smallFont = new Syncfusion.Pdf.Graphics.PdfTrueTypeFont(
                new System.Drawing.Font("Segoe UI", 8.5f), true);
            var brush = new Syncfusion.Pdf.Graphics.PdfSolidBrush(System.Drawing.Color.Black);

            g.DrawString("Forensic Tool Viewer - Evidence Summary", titleFont, brush, new PointF(0, y));
            y += 28;
            g.DrawString("CASE-2026-001", hFont, brush, new PointF(0, y));
            y += 18;
            g.DrawString($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss} (local)", bodyFont, brush, new PointF(0, y));
            y += 14;

            var fLine =
                $"Active filters - Type: {vm.SelectedEvidenceType} | App: {vm.SelectedApplication} | " +
                $"Device: {vm.SelectedDevice} | Search: '{vm.FreeTextSearch}' | " +
                $"Timeline: {(vm.TimelineStart?.ToString("yyyy-MM-dd") ?? "all")} to {(vm.TimelineEnd?.ToString("yyyy-MM-dd") ?? "all")}" +
                $" | Chart focus: {(vm.DrillValue ?? "none")}";
            g.DrawString(fLine, bodyFont, brush, new RectangleF(0, y, width, 60));
            y += 30;
            g.DrawLine(new Syncfusion.Pdf.Graphics.PdfPen(System.Drawing.Color.FromArgb(30, 136, 229), 2),
                0, y, width, y);
            y += 12;

            // ---- KPI table: the currently displayed (filtered) counts ----
            g.DrawString("Evidence by Type (current filters)", hFont, brush, new PointF(0, y));
            y += 18;

            var grid = new Syncfusion.Pdf.Grid.PdfGrid();
            grid.Columns.Add(3);
            grid.Headers.Add(1);
            grid.Headers[0].Cells[0].Value = "Evidence Type";
            grid.Headers[0].Cells[1].Value = "Records";
            grid.Headers[0].Cells[2].Value = "% of filtered set";
            grid.Headers[0].Style.Font = new Syncfusion.Pdf.Graphics.PdfTrueTypeFont(
                new System.Drawing.Font("Segoe UI", 9, System.Drawing.FontStyle.Bold), true);
            grid.Style.Font = smallFont;

            int total = vm.FilteredRecords.Count;
            foreach (var p in vm.EvidenceTypeCounts)
            {
                var row = grid.Rows.Add();
                row.Cells[0].Value = p.Name;
                row.Cells[1].Value = p.Value.ToString("N0");
                row.Cells[2].Value = total > 0 ? (100.0 * p.Value / total).ToString("F1") + "%" : "0%";
            }
            var layout = new Syncfusion.Pdf.Graphics.PdfLayoutFormat
            {
                Break = Syncfusion.Pdf.Graphics.PdfLayoutBreakType.FitElement,
                Layout = Syncfusion.Pdf.Graphics.PdfLayoutType.Paginate
            };
            grid.Draw(page, new RectangleF(0, y, width, page.GetClientSize().Height - y), layout);

            // ---- chart snapshots (visual summary) ----
            page = doc.Pages.Add();
            y = 0;
            g = page.Graphics;
            g.DrawString("Visual Summary - Chart Snapshots", hFont, brush, new PointF(0, y));
            y += 22;

            foreach (var snap in snapshots)
            {
                try
                {
                    using var ms = new System.IO.MemoryStream(snap.Png);
                    var img = Syncfusion.Pdf.Graphics.PdfImage.FromStream(ms);

                    float w = Math.Min(width, (float)img.Width * 72f / 96f);
                    float h = w * img.Height / img.Width;
                    if (y + h > page.GetClientSize().Height)
                    {
                        page = doc.Pages.Add();
                        y = 0;
                        g = page.Graphics;
                    }
                    g.DrawString(snap.Title, bodyFont, brush, new PointF(0, y));
                    y += 16;
                    g.DrawImage(img, new RectangleF(0, y, w, h));
                    y += h + 14;
                }
                catch { /* unusable snapshot - skip */ }
            }

            // footer
            for (int i = 0; i < doc.Pages.Count; i++)
            {
                var p = doc.Pages[i];
                p.Graphics.DrawString(
                    $"Forensic Tool Viewer POC - CASE-2026-001 - page {i + 1}/{doc.Pages.Count}",
                    smallFont, brush,
                    new PointF(0, p.GetClientSize().Height - 10));
            }

            doc.Save(path);
        }
    }
}
