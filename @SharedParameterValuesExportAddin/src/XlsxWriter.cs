using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace SharedParameterValuesExportAddin
{
        public static class XlsxWriter
    {
        public static void Write(string filePath, string worksheetName, IList<IList<object>> matrix)
        {
            Write(filePath, worksheetName, matrix, null);
        }

        public static void Write(string filePath, string worksheetName, IList<IList<object>> matrix, IList<ParameterSelection> parameters)
        {
            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            using (ZipArchive archive = ZipFile.Open(filePath, ZipArchiveMode.Create))
            {
                WriteEntry(archive, "[Content_Types].xml", ContentTypesXml);
                WriteEntry(archive, "_rels/.rels", RootRelationshipsXml);
                WriteEntry(archive, "docProps/core.xml", BuildCoreXml());
                WriteEntry(archive, "docProps/app.xml", AppXml);
                WriteEntry(archive, "xl/workbook.xml", BuildWorkbookXml(GetSafeSheetName(worksheetName)));
                WriteEntry(archive, "xl/_rels/workbook.xml.rels", WorkbookRelationshipsXml);
                WriteEntry(archive, "xl/styles.xml", StylesXml);
                WriteEntry(archive, "xl/worksheets/sheet1.xml", BuildSheetXml(matrix, parameters));
            }
        }

        private static void WriteEntry(ZipArchive archive, string entryName, string content)
        {
            ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using (Stream stream = entry.Open())
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(content);
            }
        }

        private static string BuildSheetXml(IList<IList<object>> matrix, IList<ParameterSelection> parameters)
        {
            var rows = new StringBuilder();

            for (int rowIndex = 0; rowIndex < matrix.Count; rowIndex++)
            {
                rows.Append("<row r=\"").Append(rowIndex + 1).Append("\">");
                IList<object> row = matrix[rowIndex];

                for (int columnIndex = 0; columnIndex < row.Count; columnIndex++)
                {
                    string cellReference = GetCellReference(rowIndex + 1, columnIndex + 1);
                    int? styleIndex = GetStyleIndex(rowIndex, columnIndex, parameters);
                    rows.Append(BuildCellXml(cellReference, row[columnIndex], styleIndex));
                }

                rows.Append("</row>");
            }

            string dimensionRef = "A1";
            if (matrix.Count > 0 && matrix[0].Count > 0)
            {
                dimensionRef = "A1:" + GetCellReference(matrix.Count, matrix[0].Count);
            }

            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                   string.Format("<dimension ref=\"{0}\"/>", dimensionRef) +
                   "<sheetViews><sheetView workbookViewId=\"0\"/></sheetViews>" +
                   "<sheetFormatPr defaultRowHeight=\"15\"/>" +
                   "<sheetData>" + rows + "</sheetData>" +
                   "</worksheet>";
        }

        private static int? GetStyleIndex(int rowIndex, int columnIndex, IList<ParameterSelection> parameters)
        {
            if (rowIndex == 0 && columnIndex >= 5 && parameters != null)
            {
                int parameterIndex = columnIndex - 5;
                if (parameterIndex >= 0 && parameterIndex < parameters.Count)
                {
                    return parameters[parameterIndex].Origin == ParameterOrigin.Project ? 2 : 3;
                }
            }

            return columnIndex == 0 ? 1 : (int?)null;
        }

        private static string BuildCellXml(string cellReference, object value, int? styleIndex)
        {
            string styleAttribute = styleIndex.HasValue ? string.Format(" s=\"{0}\"", styleIndex.Value) : string.Empty;
            string text = value == null ? string.Empty : value.ToString().Trim();

            return string.Format(
                "<c r=\"{0}\"{1} t=\"inlineStr\"><is>{2}</is></c>",
                cellReference,
                styleAttribute,
                BuildInlineStringXml(text));
        }

        private static string BuildInlineStringXml(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "<t></t>";
            }

            var xml = new StringBuilder();
            foreach (string run in SplitLanguageRuns(text))
            {
                string fontName = ContainsKorean(run) ? "Malgun Gothic" : "Arial";
                xml.Append("<r><rPr><rFont val=\"")
                    .Append(fontName)
                    .Append("\"/><sz val=\"11\"/></rPr><t xml:space=\"preserve\">")
                    .Append(SecurityElement.Escape(run))
                    .Append("</t></r>");
            }

            return xml.ToString();
        }

        private static IEnumerable<string> SplitLanguageRuns(string text)
        {
            int startIndex = 0;
            bool currentIsKorean = IsKorean(text[0]);

            for (int index = 1; index < text.Length; index++)
            {
                bool isKorean = IsKorean(text[index]);
                if (isKorean != currentIsKorean)
                {
                    yield return text.Substring(startIndex, index - startIndex);
                    startIndex = index;
                    currentIsKorean = isKorean;
                }
            }

            yield return text.Substring(startIndex);
        }

        private static bool ContainsKorean(string text)
        {
            foreach (char value in text)
            {
                if (IsKorean(value))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsKorean(char value)
        {
            return (value >= 0xAC00 && value <= 0xD7AF) ||
                   (value >= 0x1100 && value <= 0x11FF) ||
                   (value >= 0x3130 && value <= 0x318F) ||
                   (value >= 0xA960 && value <= 0xA97F) ||
                   (value >= 0xD7B0 && value <= 0xD7FF);
        }

        private static string BuildWorkbookXml(string sheetName)
        {
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
                   "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                   string.Format("<sheets><sheet name=\"{0}\" sheetId=\"1\" r:id=\"rId1\"/></sheets>", SecurityElement.Escape(sheetName)) +
                   "</workbook>";
        }

        private static string BuildCoreXml()
        {
            string createdText = DateTime.UtcNow.ToString("s") + "Z";
            return "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                   "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" " +
                   "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" " +
                   "xmlns:dcterms=\"http://purl.org/dc/terms/\" " +
                   "xmlns:dcmitype=\"http://purl.org/dc/dcmitype/\" " +
                   "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
                   "<dc:creator>Codex</dc:creator>" +
                   "<cp:lastModifiedBy>Codex</cp:lastModifiedBy>" +
                   string.Format("<dcterms:created xsi:type=\"dcterms:W3CDTF\">{0}</dcterms:created>", createdText) +
                   string.Format("<dcterms:modified xsi:type=\"dcterms:W3CDTF\">{0}</dcterms:modified>", createdText) +
                   "</cp:coreProperties>";
        }

        private static string GetCellReference(int rowIndex, int columnIndex)
        {
            return GetColumnName(columnIndex) + rowIndex;
        }

        private static string GetColumnName(int columnIndex)
        {
            string name = string.Empty;
            int current = columnIndex;

            while (current > 0)
            {
                current--;
                name = (char)('A' + current % 26) + name;
                current /= 26;
            }

            return name;
        }

        private static string GetSafeSheetName(string value)
        {
            string sheetName = string.IsNullOrWhiteSpace(value) ? "Sheet1" : value.Trim();
            foreach (char invalid in new[] { '[', ']', '*', '?', '/', '\\', ':' })
            {
                sheetName = sheetName.Replace(invalid, '_');
            }

            if (sheetName.Length > 31)
            {
                sheetName = sheetName.Substring(0, 31);
            }

            return sheetName.Length == 0 ? "Sheet1" : sheetName;
        }

        private const string ContentTypesXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
            "<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>" +
            "<Override PartName=\"/docProps/app.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.extended-properties+xml\"/>" +
            "</Types>";

        private const string RootRelationshipsXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>" +
            "<Relationship Id=\"rId3\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties\" Target=\"docProps/app.xml\"/>" +
            "</Relationships>";

        private const string WorkbookRelationshipsXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
            "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
            "</Relationships>";

        private const string AppXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\" " +
            "xmlns:vt=\"http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes\">" +
            "<Application>Revit Shared Parameter Export</Application>" +
            "</Properties>";

        private const string StylesXml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
            "<fonts count=\"1\"><font><sz val=\"11\"/><color theme=\"1\"/><name val=\"Arial\"/><family val=\"2\"/></font></fonts>" +
            "<fills count=\"5\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFFFF2CC\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFD9EAF7\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFD9EAD3\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>" +
            "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
            "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
            "<cellXfs count=\"4\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFill=\"1\"/>" +
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"3\" borderId=\"0\" xfId=\"0\" applyFill=\"1\"/>" +
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"4\" borderId=\"0\" xfId=\"0\" applyFill=\"1\"/></cellXfs>" +
            "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
            "</styleSheet>";
    }
}
