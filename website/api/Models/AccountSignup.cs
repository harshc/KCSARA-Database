namespace Kcsara.Database.Web.api.Models
{
    using System;
    using System.Configuration;
    using System.IO;
    using System.Net.Http;
    using System.Threading;  
    using System.Web.Http.Routing;
    using System.Web.Profile;
    using System.Web.Security;
    using log4net;
    using Kcsar.Database.Model;
    using Kcsar.Membership;


    // TODO: Rename to Account later
    public class AccountSignup
    {
        public const string APPLICANT_ROLE = "cdb.applicants";

        public string Firstname { get; set; }
        public string Middlename { get; set; }
        public string Lastname { get; set; }
        public DateTime? BirthDate { get; set; }
        public string Gender { get; set; }
        public string Email { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public Guid[] Units { get; set; }

        /// <summary>
        /// Internal method to create the user
        /// </summary>
        /// <param name="user">Membershipuser created in the controller</param>
        /// <param name="context">Instance of Db Context</param>
        /// <param name="log">Instance of the logManager</param>
        /// <returns>true if the user creation succeeds, false otherwise. Deletes the created user on failure</returns>
        internal bool CreateUser(MembershipUser user, KcsarContext context, ILog log)
        {
            try
            {               
                user.IsApproved = false;
                System.Web.Security.Membership.UpdateUser(user);

                System.Web.Security.FormsAuthenticationTicket ticket = new System.Web.Security.FormsAuthenticationTicket(this.Username, false, 5);
                Thread.CurrentPrincipal = new System.Web.Security.RolePrincipal(new System.Web.Security.FormsIdentity(ticket));

                Member newMember = new Member
                        {
                            FirstName = this.Firstname,
                            LastName = this.Lastname,
                            Status = MemberStatus.Applicant,
                            Username = this.Email
                        };

                context.Members.Add(newMember);

                var email = new PersonContact
                {
                    Person = newMember,
                    Type = "email",
                    Value = this.Email,
                    Priority = 0
                };

                context.PersonContact.Add(email);

                if (this.Units != null)
                {
                    foreach (Guid unitId in this.Units)
                    {
                        UnitsController.RegisterApplication(context, unitId, newMember);
                    }
                }

                var profile = ProfileBase.Create(this.Username) as KcsarUserProfile;
                if (profile != null)
                {
                    profile.FirstName = this.Firstname;
                    profile.LastName = this.Lastname;
                    profile.LinkKey = newMember.Id.ToString();
                    profile.Save();
                }

                if (!System.Web.Security.Roles.RoleExists(APPLICANT_ROLE))
                {
                    System.Web.Security.Roles.CreateRole(APPLICANT_ROLE);
                }
                System.Web.Security.Roles.AddUserToRole(this.Username, APPLICANT_ROLE);

                context.SaveChanges();
            }
            catch(Exception ex)
            {
                log.Error(ex.ToString());
                var existingUser = Membership.GetUser(this.Username);
                if (existingUser != null)
                {
                    Membership.DeleteUser(existingUser.UserName);
                }

                return false;
            }

            return true;
        }
    }
}