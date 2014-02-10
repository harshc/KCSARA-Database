namespace Kcsara.Database.Web.api.Helpers
{
    using Kcsara.Database.Web.api.Models;
    using System.Text.RegularExpressions;   

    public static class ValidationHelpers 
    {
        public static bool Validate(this AccountSignup accountData)
        {
            bool isValid = true;
            if (string.IsNullOrWhiteSpace(accountData.Firstname))
                return false;
            if (string.IsNullOrWhiteSpace(accountData.Lastname))
                return false;
            if (string.IsNullOrWhiteSpace(accountData.Email))
                return false;
            if (!Regex.IsMatch(accountData.Email, @"^\S+@\S+\.\S+$"))
                return false;
            if (string.IsNullOrWhiteSpace(accountData.Password))
                return false;
            if (accountData.Password.Length < 6)
                return false;
            if (accountData.Password.Length > 64)
                return false;

            return isValid;
        }
    }
}