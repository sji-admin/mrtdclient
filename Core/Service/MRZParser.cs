using cmrtd.Core.Model;

namespace cmrtd.Core.Service
{
    public class MRZParser
    {
        public MRZData ParseTD3(string mrz)
        {
            // Bersihkan input (hapus newline dan spasi tidak perlu)
            mrz = mrz.Replace("\n", "").Replace("\r", "").Trim();

            if (mrz.Length != 88)
                throw new ArgumentException($"Invalid MRZ length. Expected 88 characters but got {mrz.Length}.");

            string line1 = mrz.Substring(0, 44);
            string line2 = mrz.Substring(44);

            string docCode = line1.Substring(0, 2).Replace("<", "").Trim();
            string issuingCountry = line1.Substring(2, 3);
            string[] nameParts = line1.Substring(5).Split(new[] { "<<" }, StringSplitOptions.None);
            string surname = nameParts[0].Replace("<", " ").Trim();
            string givenNames = nameParts.Length > 1 ? nameParts[1].Replace("<", " ").Trim() : "";

            string passportNumber = line2.Substring(0, 9).Replace("<", "");
            string nationality = line2.Substring(10, 3);
            string birthDate = FormatDate(line2.Substring(13, 6));
            string sex = line2.Substring(20, 1);
            string expiryDate = FormatDate(line2.Substring(21, 6));
            string personalNumber = line2.Substring(28, 14).Replace("<", "");

            return new MRZData
            {
                DocCode = docCode,
                IssuingCountry = issuingCountry,
                Surname = surname,
                GivenNames = givenNames,
                PassportNumber = passportNumber,
                Nationality = nationality,
                BirthDate = birthDate,
                Sex = sex,
                ExpiryDate = expiryDate,
                PersonalNumber = personalNumber
            };
        }

        private string FormatDate(string yymmdd)
        {
            int year = int.Parse(yymmdd.Substring(0, 2));
            int month = int.Parse(yymmdd.Substring(2, 2));
            int day = int.Parse(yymmdd.Substring(4, 2));

            int fullYear = (year >= 50) ? 1900 + year : 2000 + year;

            return $"{fullYear:D4}-{month:D2}-{day:D2}";
        }
    }
}
