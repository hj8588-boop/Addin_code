using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace SharedParameterValuesExportAddin
{
    public static class SpreadsheetReader
    {
        private const string MainNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private const string RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private const string PackageRelationshipNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";

        public static IList<IList<string>> Read(string filePath, string sheetName)
        {
            string extension = Path.GetExtension(filePath).ToLowerInvariant();
            if (extension == ".csv")
            {
                return ReadCsv(filePath);
            }

            if (extension == ".xlsx" || extension == ".xlsm")
            {
                return ReadXlsx(filePath, sheetName);
            }

            throw new InvalidOperationException("Only .csv, .xlsx, and .xlsm files are supported.");
        }

        private static IList<IList<string>> ReadCsv(string filePath)
        {
            var rows = new List<IList<string>>();
            foreach (string line in File.ReadAllLines(filePath, Encoding.UTF8))
            {
                rows.Add(ParseCsvLine(line));
            }

            return rows;
        }

        private static IList<string> ParseCsvLine(string line)
        {
            var values = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;

            for (int index = 0; index < line.Length; index++)
            {
                char character = line[index];
                if (character == '"')
                {
                    if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                    {
                        current.Append('"');
                        index++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (character == ',' && !inQuotes)
                {
                    values.Add(current.ToString());
                    current.Length = 0;
                }
                else
                {
                    current.Append(character);
                }
            }

            values.Add(current.ToString());
            return values;
        }

        private static IList<IList<string>> ReadXlsx(string filePath, string sheetName)
        {
            using (ZipArchive archive = ZipFile.OpenRead(filePath))
            {
                IList<string> sharedStrings = GetSharedStrings(archive);
                string sheetPath = GetSheetPath(archive, sheetName);
                XmlDocument sheetDocument = LoadXml(archive, sheetPath);

                var manager = new XmlNamespaceManager(sheetDocument.NameTable);
                manager.AddNamespace("x", MainNamespace);

                var matrix = new List<IList<string>>();
                XmlNodeList rowNodes = sheetDocument.SelectNodes("//x:sheetData/x:row", manager);

                foreach (XmlNode rowNode in rowNodes)
                {
                    var rowValues = new List<string>();
                    int currentColumn = 1;

                    foreach (XmlNode cellNode in rowNode.SelectNodes("x:c", manager))
                    {
                        string cellReference = GetAttribute(cellNode, "r");
                        int columnIndex = string.IsNullOrEmpty(cellReference)
                            ? currentColumn
                            : GetColumnIndexFromReference(cellReference);

                        while (currentColumn < columnIndex)
                        {
                            rowValues.Add(string.Empty);
                            currentColumn++;
                        }

                        rowValues.Add(GetCellText(cellNode, sharedStrings, manager));
                        currentColumn++;
                    }

                    matrix.Add(rowValues);
                }

                return matrix;
            }
        }

        private static XmlDocument LoadXml(ZipArchive archive, string internalPath)
        {
            ZipArchiveEntry entry = archive.GetEntry(internalPath);
            if (entry == null)
            {
                throw new InvalidOperationException("XLSX part not found: " + internalPath);
            }

            var document = new XmlDocument();
            using (Stream stream = entry.Open())
            {
                document.Load(stream);
            }

            return document;
        }

        private static IList<string> GetSharedStrings(ZipArchive archive)
        {
            var values = new List<string>();
            ZipArchiveEntry entry = archive.GetEntry("xl/sharedStrings.xml");
            if (entry == null)
            {
                return values;
            }

            XmlDocument document = LoadXml(archive, "xl/sharedStrings.xml");
            var manager = new XmlNamespaceManager(document.NameTable);
            manager.AddNamespace("x", MainNamespace);

            foreach (XmlNode itemNode in document.SelectNodes("//x:si", manager))
            {
                var text = new StringBuilder();
                foreach (XmlNode textNode in itemNode.SelectNodes(".//x:t", manager))
                {
                    text.Append(textNode.InnerText);
                }

                values.Add(text.ToString());
            }

            return values;
        }

        private static string GetSheetPath(ZipArchive archive, string worksheetName)
        {
            string safeSheetName = string.IsNullOrWhiteSpace(worksheetName) ? "SharedParameters" : worksheetName.Trim();
            XmlDocument workbook = LoadXml(archive, "xl/workbook.xml");
            var workbookManager = new XmlNamespaceManager(workbook.NameTable);
            workbookManager.AddNamespace("x", MainNamespace);
            workbookManager.AddNamespace("r", RelationshipNamespace);

            string relationshipId = null;
            foreach (XmlNode sheetNode in workbook.SelectNodes("//x:sheet", workbookManager))
            {
                if (string.Equals(GetAttribute(sheetNode, "name"), safeSheetName, StringComparison.OrdinalIgnoreCase))
                {
                    relationshipId = GetAttribute(sheetNode, "r:id");
                    break;
                }
            }

            if (string.IsNullOrEmpty(relationshipId))
            {
                throw new InvalidOperationException("Worksheet not found: " + safeSheetName);
            }

            XmlDocument relationships = LoadXml(archive, "xl/_rels/workbook.xml.rels");
            var relationshipManager = new XmlNamespaceManager(relationships.NameTable);
            relationshipManager.AddNamespace("rel", PackageRelationshipNamespace);

            foreach (XmlNode relationshipNode in relationships.SelectNodes("//rel:Relationship", relationshipManager))
            {
                if (GetAttribute(relationshipNode, "Id") == relationshipId)
                {
                    string target = GetAttribute(relationshipNode, "Target").Replace("\\", "/");
                    return target.StartsWith("xl/", StringComparison.OrdinalIgnoreCase) ? target : "xl/" + target;
                }
            }

            throw new InvalidOperationException("Worksheet relationship not found: " + safeSheetName);
        }

        private static string GetCellText(XmlNode cellNode, IList<string> sharedStrings, XmlNamespaceManager manager)
        {
            string cellType = GetAttribute(cellNode, "t");
            if (cellType == "inlineStr")
            {
                var text = new StringBuilder();
                foreach (XmlNode textNode in cellNode.SelectNodes(".//x:t", manager))
                {
                    text.Append(textNode.InnerText);
                }

                return text.ToString();
            }

            XmlNode valueNode = cellNode.SelectSingleNode("x:v", manager);
            if (valueNode == null)
            {
                return string.Empty;
            }

            string rawValue = valueNode.InnerText;
            if (cellType == "s")
            {
                int sharedStringIndex;
                if (int.TryParse(rawValue, out sharedStringIndex) &&
                    sharedStringIndex >= 0 &&
                    sharedStringIndex < sharedStrings.Count)
                {
                    return sharedStrings[sharedStringIndex];
                }

                return string.Empty;
            }

            return rawValue;
        }

        private static int GetColumnIndexFromReference(string cellReference)
        {
            int columnIndex = 0;
            foreach (char character in cellReference)
            {
                if (!char.IsLetter(character))
                {
                    break;
                }

                columnIndex = (columnIndex * 26) + (char.ToUpperInvariant(character) - 64);
            }

            return columnIndex;
        }

        private static string GetAttribute(XmlNode node, string name)
        {
            XmlAttribute attribute = node.Attributes == null ? null : node.Attributes[name];
            return attribute == null ? string.Empty : attribute.Value;
        }
    }
}
