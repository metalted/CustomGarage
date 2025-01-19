using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace CustomGarage
{
    public class ZeeplevelFile
    {
        public ZeeplevelHeader Header { get; private set; }
        public List<ZeeplevelBlock> Blocks { get; private set; }
        public string FileName { get; private set; }
        public string FilePath { get; private set; }
        public bool Valid { get; private set; }

        //Create a valid but empty zeeplevel file.
        public ZeeplevelFile()
        {
            GenerateBaseFile();
        }

        //Create a zeeplevel file by reading it from a path, and then reading it from the CSV data.
        public ZeeplevelFile(string path)
        {
            GenerateBaseFile();
            ReadFromPath(path);
        }

        //Create a zeeplevel file from the csv data directly.
        public ZeeplevelFile(string[] csvData)
        {
            GenerateBaseFile();
            ReadCSVData(csvData);
        }

        private void GenerateBaseFile()
        {
            Header = new ZeeplevelHeader();
            Blocks = new List<ZeeplevelBlock>();
            FileName = "";
            FilePath = "";
            Valid = true;
        }

        private void ReadFromPath(string path)
        {
            string[] csvData;

            try
            {
                csvData = File.ReadAllLines(path);
            }
            catch (Exception ex)
            {
                Debug.LogError(ex.Message);
                Valid = false;
                return;
            }

            FileName = Path.GetFileNameWithoutExtension(path);
            FilePath = path;

            Valid = ReadCSVData(csvData);
        }

        private bool ReadCSVData(string[] csvData)
        {
            if (csvData.Length < 3)
            {
                return false;
            }

            // Read the first 3 lines into the header
            string[] headerData = new string[3];
            Array.Copy(csvData, 0, headerData, 0, 3);
            Header.ReadCSVData(headerData);

            if (!Header.Valid)
            {
                return false;
            }

            Blocks.Clear();

            // Read the remaining lines into blocks
            for (int i = 3; i < csvData.Length; i++)
            {
                ZeeplevelBlock block = new ZeeplevelBlock();
                block.ReadCSVString(csvData[i]);

                if (block.Valid)
                {
                    Blocks.Add(block);
                }
                else
                {
                    return false;
                }
            }

            if (Blocks.Count == 0)
            {
                return false;
            }

            return true;
        }

        public void SetPlayerName(string playerName)
        {
            Header.GenerateNewUUID(playerName, Blocks.Count);
        }

        public void SetFileName(string fileName)
        {
            FileName = fileName;
        }

        public void SetPath(string path)
        {
            FilePath = path;
            FileName = Path.GetFileNameWithoutExtension(path);
        }

        public void ImportBlockProperties(List<BlockProperties> blockProperties)
        {
            Blocks.Clear();

            foreach (BlockProperties bp in blockProperties)
            {
                ZeeplevelBlock block = new ZeeplevelBlock();
                block.ReadBlockProperties(bp);
                if (block.Valid)
                {
                    Blocks.Add(block);
                }
            }

            Header.GenerateNewUUID(Header.PlayerName, Blocks.Count);
        }

        public string[] ToCSV()
        {
            List<string> csvLines = new List<string>();

            // Add the header CSV lines
            csvLines.AddRange(Header.ToCSV());

            // Add each block's CSV representation
            foreach (var block in Blocks)
            {
                var blockCsv = block.ToCSV();
                if (!string.IsNullOrWhiteSpace(blockCsv))
                {
                    csvLines.Add(blockCsv);
                }
            }

            // Remove any empty lines
            csvLines = csvLines.Where(line => !string.IsNullOrWhiteSpace(line)).ToList();

            return csvLines.ToArray();
        }
    }
}
