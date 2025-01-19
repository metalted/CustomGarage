using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CustomGarage
{
    public class ZeeplevelHeader
    {
        public string SceneName { get; private set; }
        public string PlayerName { get; private set; }
        public string UUID { get; private set; }
        public float[] CameraProperties { get; private set; }
        public float AuthorTime { get; private set; }
        public string AuthorTimeString { get; private set; }
        public float GoldTime { get; private set; }
        public float SilverTime { get; private set; }
        public float BronzeTime { get; private set; }
        public int Skybox { get; private set; }
        public int Ground { get; private set; }
        public bool Valid { get; private set; }

        public ZeeplevelHeader()
        {
            SceneName = "LevelEditor2";
            PlayerName = "Bouwerman";
            UUID = GenerateUUID(PlayerName, 0);
            CameraProperties = new float[] { 0, 0, 0, 0, 0, 0, 0, 0 };
            AuthorTime = 0;
            AuthorTimeString = "invalid track";
            GoldTime = 0;
            SilverTime = 0;
            BronzeTime = 0;
            Skybox = 0;
            Ground = -1;
            Valid = true;
        }

        public void ReadCSVData(string[] csvData)
        {
            if (csvData.Length != 3)
            {
                Valid = false;
                return;
            }

            for (int i = 0; i < csvData.Length; i++)
            {
                string[] values = csvData[i].Split(",");
                if (i == 0)
                {
                    if (values.Length != 3)
                    {
                        Valid = false;
                        break;
                    }

                    SceneName = values[0];
                    PlayerName = values[1];
                    UUID = values[2];
                }
                else if (i == 1)
                {
                    if (values.Length != 8)
                    {
                        Valid = false;
                        break;
                    }

                    for (int j = 0; j < 8; j++)
                    {
                        CameraProperties[j] = ParseFloat(values[j]);
                    }
                }
                else if (i == 2)
                {
                    if (values.Length != 6)
                    {
                        Valid = false;
                        break;
                    }

                    AuthorTime = ParseFloat(values[0]);
                    AuthorTimeString = AuthorTime == 0 ? "invalid track" : "";

                    GoldTime = ParseFloat(values[1]);
                    SilverTime = ParseFloat(values[2]);
                    BronzeTime = ParseFloat(values[3]);
                    Skybox = ParseInt(values[4]);
                    if (Skybox == -1) { Skybox = 0; }
                    Ground = ParseInt(values[0]);
                }
            }
        }

        public void GenerateNewUUID(string playerName, int objectCount)
        {
            UUID = GenerateUUID(playerName, objectCount);
            PlayerName = playerName;
        }

        private string GenerateUUID(string playerName, int objectCount)
        {
            // Get the current date and time
            string date = DateTime.Now.ToString("ddMMyyyy");
            string time = DateTime.Now.ToString("HHmmssfff");

            // Generate a 12-digit random number that does not start with 0
            Random random = new Random();
            string randomNumber = (random.Next(1, 10).ToString() + random.Next(0, 1000000000).ToString("D9"));

            // Combine all parts to form the UUID
            string uuid = $"{date}-{time}-{playerName}-{randomNumber}-{objectCount}";

            return uuid;
        }

        private int ParseInt(string value)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : -1;
        }

        private float ParseFloat(string value)
        {
            return float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out float result) ? result : 0.0f;
        }

        public string[] ToCSV()
        {
            // First line: SceneName, PlayerName, UUID
            string firstLine = $"{SceneName},{PlayerName},{UUID}";

            // Second line: CameraProperties
            string secondLine = string.Join(",", CameraProperties);

            // Third line: AuthorTime (or AuthorTimeString), GoldTime, SilverTime, BronzeTime, Skybox, Ground
            string authorTimeValue = AuthorTimeString == "invalid track" ? AuthorTimeString : AuthorTime.ToString(CultureInfo.InvariantCulture);
            string thirdLine = $"{authorTimeValue},{GoldTime},{SilverTime},{BronzeTime},{Skybox},{Ground}";

            // Return an array of strings
            return new string[] { firstLine, secondLine, thirdLine };
        }
    }
}
