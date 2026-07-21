using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RevitSheetBatchEditor.Engine
{
    public static class SimpleXlsxService
    {
        private const string SpreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private const string OfficeRelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        public static void Write(string path, ExcelTable table)
        {
            if (File.Exists(path)) File.Delete(path);
            using (Package package = Package.Open(path, FileMode.Create, FileAccess.ReadWrite))
            {
                PackagePart workbook = CreatePart(package, "/xl/workbook.xml",
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
                PackagePart worksheet = CreatePart(package, "/xl/worksheets/sheet1.xml",
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
                PackagePart styles = CreatePart(package, "/xl/styles.xml",
                    "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");

                PackageRelationship workbookRelationship = package.CreateRelationship(workbook.Uri,
                    TargetMode.Internal, "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument");
                workbook.CreateRelationship(worksheet.Uri, TargetMode.Internal,
                    "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet", "rId1");
                workbook.CreateRelationship(styles.Uri, TargetMode.Internal,
                    "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles", "rId2");

                WriteXml(workbook, CreateWorkbookDocument());
                WriteXml(styles, CreateStylesDocument());
                WriteXml(worksheet, CreateWorksheetDocument(table));
            }
        }

        public static ExcelTable Read(string path)
        {
            using (Package package = Package.Open(path, FileMode.Open, FileAccess.Read))
            {
                PackagePart worksheet = package.GetParts().FirstOrDefault(x =>
                    x.ContentType == "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
                if (worksheet == null) throw new InvalidDataException("No worksheet was found in the Excel file.");

                List<string> sharedStrings = ReadSharedStrings(package);
                XDocument document;
                using (Stream stream = worksheet.GetStream(FileMode.Open, FileAccess.Read)) document = XDocument.Load(stream);
                XNamespace ns = SpreadsheetNs;
                List<List<string>> rawRows = document.Descendants(ns + "row")
                    .Select(row => ReadRow(row, ns, sharedStrings)).ToList();
                if (rawRows.Count == 0) throw new InvalidDataException("The Excel worksheet is empty.");

                return new ExcelTable
                {
                    Headers = rawRows[0],
                    Rows = rawRows.Skip(1).Where(x => x.Any(value => !string.IsNullOrWhiteSpace(value))).ToList()
                };
            }
        }

        private static PackagePart CreatePart(Package package, string uri, string contentType)
        {
            return package.CreatePart(new Uri(uri, UriKind.Relative), contentType, CompressionOption.Maximum);
        }

        private static void WriteXml(PackagePart part, XDocument document)
        {
            using (Stream stream = part.GetStream(FileMode.Create, FileAccess.Write)) document.Save(stream);
        }

        private static XDocument CreateWorkbookDocument()
        {
            XNamespace ns = SpreadsheetNs;
            XNamespace rel = OfficeRelNs;
            return new XDocument(new XElement(ns + "workbook",
                new XAttribute(XNamespace.Xmlns + "r", rel),
                new XElement(ns + "sheets",
                    new XElement(ns + "sheet", new XAttribute("name", "Revit Sheet Parameters"),
                        new XAttribute("sheetId", "1"), new XAttribute(rel + "id", "rId1")))));
        }

        private static XDocument CreateWorksheetDocument(ExcelTable table)
        {
            XNamespace ns = SpreadsheetNs;
            var allRows = new List<List<string>> { table.Headers };
            allRows.AddRange(table.Rows);
            int columnCount = table.Headers.Count;
            var sheetData = new XElement(ns + "sheetData");
            for (int rowIndex = 0; rowIndex < allRows.Count; rowIndex++)
            {
                var row = new XElement(ns + "row", new XAttribute("r", rowIndex + 1));
                for (int columnIndex = 0; columnIndex < allRows[rowIndex].Count; columnIndex++)
                {
                    string cellReference = ColumnName(columnIndex + 1) + (rowIndex + 1);
                    row.Add(new XElement(ns + "c", new XAttribute("r", cellReference),
                        new XAttribute("t", "inlineStr"), new XAttribute("s", rowIndex == 0 ? "1" : "0"),
                        new XElement(ns + "is", new XElement(ns + "t",
                            new XAttribute(XNamespace.Xml + "space", "preserve"), allRows[rowIndex][columnIndex] ?? string.Empty))));
                }
                sheetData.Add(row);
            }

            return new XDocument(new XElement(ns + "worksheet",
                new XElement(ns + "sheetViews", new XElement(ns + "sheetView", new XAttribute("workbookViewId", "0"),
                    new XElement(ns + "pane", new XAttribute("ySplit", "1"), new XAttribute("topLeftCell", "A2"),
                        new XAttribute("activePane", "bottomLeft"), new XAttribute("state", "frozen")))),
                new XElement(ns + "cols", Enumerable.Range(1, columnCount).Select(index =>
                    new XElement(ns + "col", new XAttribute("min", index), new XAttribute("max", index),
                        new XAttribute("width", index <= 3 ? "22" : "28"), new XAttribute("customWidth", "1")))),
                sheetData,
                new XElement(ns + "autoFilter", new XAttribute("ref", "A1:" + ColumnName(columnCount) + Math.Max(1, allRows.Count)))));
        }

        private static XDocument CreateStylesDocument()
        {
            XNamespace ns = SpreadsheetNs;
            return new XDocument(new XElement(ns + "styleSheet",
                new XElement(ns + "fonts", new XAttribute("count", "2"),
                    new XElement(ns + "font", new XElement(ns + "sz", new XAttribute("val", "10")), new XElement(ns + "name", new XAttribute("val", "Segoe UI"))),
                    new XElement(ns + "font", new XElement(ns + "b"), new XElement(ns + "color", new XAttribute("rgb", "FFFFFFFF")),
                        new XElement(ns + "sz", new XAttribute("val", "10")), new XElement(ns + "name", new XAttribute("val", "Segoe UI")))),
                new XElement(ns + "fills", new XAttribute("count", "3"),
                    new XElement(ns + "fill", new XElement(ns + "patternFill", new XAttribute("patternType", "none"))),
                    new XElement(ns + "fill", new XElement(ns + "patternFill", new XAttribute("patternType", "gray125"))),
                    new XElement(ns + "fill", new XElement(ns + "patternFill", new XAttribute("patternType", "solid"),
                        new XElement(ns + "fgColor", new XAttribute("rgb", "FF1F4E78")), new XElement(ns + "bgColor", new XAttribute("indexed", "64"))))),
                new XElement(ns + "borders", new XAttribute("count", "1"), new XElement(ns + "border",
                    new XElement(ns + "left"), new XElement(ns + "right"), new XElement(ns + "top"), new XElement(ns + "bottom"), new XElement(ns + "diagonal"))),
                new XElement(ns + "cellStyleXfs", new XAttribute("count", "1"), new XElement(ns + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", "0"), new XAttribute("fillId", "0"), new XAttribute("borderId", "0"))),
                new XElement(ns + "cellXfs", new XAttribute("count", "2"),
                    new XElement(ns + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", "0"), new XAttribute("fillId", "0"), new XAttribute("borderId", "0"), new XAttribute("xfId", "0")),
                    new XElement(ns + "xf", new XAttribute("numFmtId", "0"), new XAttribute("fontId", "1"), new XAttribute("fillId", "2"), new XAttribute("borderId", "0"), new XAttribute("xfId", "0"), new XAttribute("applyFill", "1"), new XAttribute("applyFont", "1"))),
                new XElement(ns + "cellStyles", new XAttribute("count", "1"), new XElement(ns + "cellStyle", new XAttribute("name", "Normal"), new XAttribute("xfId", "0"), new XAttribute("builtinId", "0")))));
        }

        private static List<string> ReadSharedStrings(Package package)
        {
            PackagePart part = package.GetParts().FirstOrDefault(x =>
                x.ContentType == "application/vnd.openxmlformats-officedocument.spreadsheetml.sharedStrings+xml");
            if (part == null) return new List<string>();
            XDocument document;
            using (Stream stream = part.GetStream(FileMode.Open, FileAccess.Read)) document = XDocument.Load(stream);
            XNamespace ns = SpreadsheetNs;
            return document.Descendants(ns + "si").Select(x => string.Concat(x.Descendants(ns + "t").Select(t => t.Value))).ToList();
        }

        private static List<string> ReadRow(XElement row, XNamespace ns, IList<string> sharedStrings)
        {
            var values = new List<string>();
            foreach (XElement cell in row.Elements(ns + "c"))
            {
                int index = ColumnIndex((string)cell.Attribute("r"));
                while (values.Count <= index) values.Add(string.Empty);
                string type = (string)cell.Attribute("t");
                string value;
                if (type == "inlineStr") value = string.Concat(cell.Descendants(ns + "t").Select(x => x.Value));
                else
                {
                    value = (string)cell.Element(ns + "v") ?? string.Empty;
                    int sharedIndex;
                    if (type == "s" && int.TryParse(value, out sharedIndex) && sharedIndex >= 0 && sharedIndex < sharedStrings.Count)
                        value = sharedStrings[sharedIndex];
                }
                values[index] = value;
            }
            return values;
        }

        private static string ColumnName(int index)
        {
            string result = string.Empty;
            while (index > 0) { index--; result = (char)('A' + index % 26) + result; index /= 26; }
            return result;
        }

        private static int ColumnIndex(string reference)
        {
            Match match = Regex.Match(reference ?? string.Empty, "^[A-Za-z]+");
            int result = 0;
            foreach (char character in match.Value.ToUpperInvariant()) result = result * 26 + (character - 'A' + 1);
            return Math.Max(0, result - 1);
        }
    }
}
