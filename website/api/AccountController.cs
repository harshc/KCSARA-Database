/*
 * Copyright 2013-2014 Matthew Cosand
 */

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

    [HttpPost]
    public bool Verify(AccountVerify data)
    {
      if (data == null || string.IsNullOrWhiteSpace(data.Username) || string.IsNullOrWhiteSpace(data.Key))
        return false;

      var user = membership.GetUser(data.Username, false);
      if (user != null && data.Key.Equals((user.ProviderUserKey ?? "").ToString(), StringComparison.OrdinalIgnoreCase))
      {
        user.IsApproved = true;
        membership.UpdateUser(user);

        return true;
      }

      return false;
    }

    [HttpPost]
    [Authorize(Roles = "site.accounts")]
    public object GetInactiveAccounts()
    {
      DateTime now = DateTime.Now;

      var members = this.db.Members.Where(f => 
        f.Id != Guid.Empty
        && f.Username != null && !f.Username.StartsWith("-")
        && (f.Status & MemberStatus.Applicant) != MemberStatus.Applicant
        && !f.Memberships.Any(g => g.Status.IsActive && (g.EndTime == null || g.EndTime > now)))
        .Select(f => new
        {
          Username = f.Username,
          FirstName = f.FirstName,
          LastName = f.LastName,
          Id = f.Id
        })
        .OrderBy(f => f.LastName).ThenBy(f => f.FirstName)
        .ToArray();

      return members;
    }

    [HttpPost]
    [Authorize(Roles = "site.accounts")]
    public bool DisableAccounts(string[] id)
    {
      foreach (var name in id)
      {
        membership.DeleteUser(name, true);
        var member = db.Members.Single(f => f.Username == name);
        member.Username = "-" + member.Username;
      }
      db.SaveChanges();

      return true;
    }
  }
}
