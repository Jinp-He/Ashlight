using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace DataTableManager
{
    public class SheetData
    {
        public string Name;
        public List<List<string>> Rows = new List<List<string>>();
        public int ColumnCount;
        public bool Truncated;
    }

    public class WorkbookData
    {
        public string Path;
        public List<SheetData> Sheets = new List<SheetData>();
    }

    public static class XlsxReader
    {
        static readonly XNamespace NsMain = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        static readonly XNamespace NsRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        static readonly XNamespace NsPkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";

        public static WorkbookData Read(string path, int maxRows = 500)
        {
            var wb = new WorkbookData { Path = path };
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                var sst = ReadSharedStrings(zip);
                foreach (var sheet in ReadWorkbookSheets(zip))
                {
                    var entry = zip.GetEntry(sheet.target);
                    if (entry == null) continue;
                    using (var s = entry.Open())
                    {
                        wb.Sheets.Add(ParseSheet(s, sheet.name, sst, maxRows));
                    }
                }
            }
            return wb;
        }

        static List<string> ReadSharedStrings(ZipArchive zip)
        {
            var list = new List<string>();
            var entry = zip.GetEntry("xl/sharedStrings.xml");
            if (entry == null) return list;
            using (var s = entry.Open())
            {
                var doc = XDocument.Load(s);
                if (doc.Root == null) return list;
                foreach (var si in doc.Root.Elements(NsMain + "si"))
                {
                    var sb = new StringBuilder();
                    foreach (var t in si.Descendants(NsMain + "t")) sb.Append(t.Value);
                    list.Add(sb.ToString());
                }
            }
            return list;
        }

        static List<(string name, string target)> ReadWorkbookSheets(ZipArchive zip)
        {
            var result = new List<(string, string)>();
            var rels = new Dictionary<string, string>();
            var relsEntry = zip.GetEntry("xl/_rels/workbook.xml.rels");
            if (relsEntry != null)
            {
                using (var s = relsEntry.Open())
                {
                    var doc = XDocument.Load(s);
                    if (doc.Root != null)
                    {
                        foreach (var r in doc.Root.Elements(NsPkgRel + "Relationship"))
                        {
                            var id = r.Attribute("Id")?.Value;
                            var target = r.Attribute("Target")?.Value;
                            if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(target))
                                rels[id] = target;
                        }
                    }
                }
            }

            var wbEntry = zip.GetEntry("xl/workbook.xml");
            if (wbEntry == null) return result;
            using (var s = wbEntry.Open())
            {
                var doc = XDocument.Load(s);
                var sheets = doc.Root?.Element(NsMain + "sheets");
                if (sheets == null) return result;
                foreach (var sh in sheets.Elements(NsMain + "sheet"))
                {
                    var name = sh.Attribute("name")?.Value ?? "";
                    var rid = sh.Attribute(NsRel + "id")?.Value;
                    if (rid != null && rels.TryGetValue(rid, out var target))
                    {
                        var path = target.StartsWith("/") ? target.Substring(1)
                                 : target.StartsWith("xl/") ? target
                                 : "xl/" + target;
                        result.Add((name, path));
                    }
                }
            }
            return result;
        }

        static SheetData ParseSheet(Stream s, string name, List<string> sst, int maxRows)
        {
            var sd = new SheetData { Name = name };
            var doc = XDocument.Load(s);
            var sheetData = doc.Root?.Element(NsMain + "sheetData");
            if (sheetData == null) return sd;
            int rowIdx = 0;
            foreach (var rowEl in sheetData.Elements(NsMain + "row"))
            {
                if (rowIdx >= maxRows) { sd.Truncated = true; break; }
                var row = new List<string>();
                int curCol = 0;
                foreach (var cEl in rowEl.Elements(NsMain + "c"))
                {
                    int col = ParseColumn(cEl.Attribute("r")?.Value);
                    while (curCol < col - 1) { row.Add(""); curCol++; }
                    row.Add(GetCellValue(cEl, cEl.Attribute("t")?.Value, sst));
                    curCol++;
                }
                sd.Rows.Add(row);
                if (row.Count > sd.ColumnCount) sd.ColumnCount = row.Count;
                rowIdx++;
            }
            return sd;
        }

        static int ParseColumn(string cellRef)
        {
            if (string.IsNullOrEmpty(cellRef)) return 1;
            int col = 0;
            foreach (var ch in cellRef)
            {
                if (ch >= 'A' && ch <= 'Z') col = col * 26 + (ch - 'A' + 1);
                else if (ch >= 'a' && ch <= 'z') col = col * 26 + (ch - 'a' + 1);
                else break;
            }
            return col <= 0 ? 1 : col;
        }

        static string GetCellValue(XElement c, string t, List<string> sst)
        {
            if (t == "inlineStr")
            {
                var isEl = c.Element(NsMain + "is");
                if (isEl == null) return "";
                var sb = new StringBuilder();
                foreach (var tEl in isEl.Descendants(NsMain + "t")) sb.Append(tEl.Value);
                return sb.ToString();
            }
            var v = c.Element(NsMain + "v")?.Value;
            if (v == null) return "";
            if (t == "s")
            {
                if (int.TryParse(v, out var idx) && idx >= 0 && idx < sst.Count) return sst[idx];
                return "";
            }
            if (t == "b") return v == "1" ? "TRUE" : "FALSE";
            return v;
        }
    }
}
