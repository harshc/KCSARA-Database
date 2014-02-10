
namespace Kcsara.Database.Web.api
{
    using Kcsara.Database.Web.api.Helpers;
    using Kcsara.Database.Web.api.Models;
    using Kcsara.Database.Web.Services;
    using System;
    using System.Configuration;
    using System.IO;
    using System.Text.RegularExpressions;
    using System.Web.Http;
    using System.Web.Security;

    [ModelValidationFilter]
    public class AccountController : BaseApiController
    {
        public const string APPLICANT_ROLE = "cdb.applicants";

        /// <summary>
        /// 
        /// </summary>
        /// <param name="id">Username to check.</param>
        /// <returns></returns>
        [HttpPost]
        public string CheckUsername(string id)
        {
            if (id.Length < 3)
                return "Too short";
            if (id.Length > 200)
                return "Too long";
            if (!Regex.IsMatch(id, @"^[a-zA-Z0-9\.\-_]+$"))
                return "Can only contain numbers, letters, and the characters '.', '-', and '_'";

            var existing = System.Web.Security.Membership.GetUser(id);
            return (existing == null) ? "Available" : "Not Available";
        }

        [HttpPost]
        public bool CreateAccount(AccountSignup accountdata)
        {
            // Temporary Hack for back compat
            accountdata.Username = accountdata.Email;

            if (accountdata.Validate())
            {
                var user = Membership.CreateUser(accountdata.Email, accountdata.Password);
                if (accountdata.CreateUser(user, this.db, this.log))
                {
#if !DEBUG
                    string mailSubject = string.Format("{0} account verification", ConfigurationManager.AppSettings["dbNameShort"] ?? "KCSARA");
                    string mailTemplate = File.ReadAllText(Path.Combine(Path.GetDirectoryName(new Uri(typeof(AccountController).Assembly.CodeBase).LocalPath), "EmailTemplates", "new-account-verification.html"));
                    string mailBody = mailTemplate
                        .Replace("%Username%", accountdata.Username)
                        .Replace("%VerifyLink%", new Uri(this.Request.RequestUri, Url.Route("Default", 
                            new { httproute = "", controller = "Account", action = "Verify", id = accountdata.Username })).AbsoluteUri + "?key=" + user.ProviderUserKey.ToString())
                        .Replace("%WebsiteContact%", "webpage@kingcountysar.org");                    
                    
                    EmailService.SendMail(accountdata.Email, mailSubject, mailBody);                    
#endif
                }
                else
                {
                    return false;
                }
            }
            else
            {
                return false;
            }

            return true;
        }
       
        [HttpPost]
        public bool Verify(AccountVerify data)
        {
            if (data == null || string.IsNullOrWhiteSpace(data.Username) || string.IsNullOrWhiteSpace(data.Key))
                return false;

            var user = System.Web.Security.Membership.GetUser(data.Username);
            if (user != null && data.Key.Equals((user.ProviderUserKey ?? "").ToString(), StringComparison.OrdinalIgnoreCase))
            {
                user.IsApproved = true;
                System.Web.Security.Membership.UpdateUser(user);

                return true;
            }

            return false;
        }
    }
}