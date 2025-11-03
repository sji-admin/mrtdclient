namespace cmrtd.Core.Model
{
    public class MRZData
    {
        public string DocCode { get; set; }
        public string IssuingCountry { get; set; }
        public string Surname { get; set; }
        public string GivenNames { get; set; }
        public string PassportNumber { get; set; }
        public string Nationality { get; set; }
        public string BirthDate { get; set; }   // bisa string atau DateTime
        public string Sex { get; set; }
        public string ExpiryDate { get; set; }
        public string PersonalNumber { get; set; }
    }
}
