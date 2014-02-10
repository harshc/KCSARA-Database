
namespace Kcsara.Database.Web.Controllers
{
    using Kcsar.Database;
    using Kcsar.Database.Model;
    using Kcsara.Database.Web.Model;
    using System;
    using System.Collections.Generic;
    using System.Configuration;
    using System.Data.Entity.Infrastructure;
    using System.Data.Objects.SqlClient;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Web.Mvc;
    using System.Xml;

    public partial class TrainingController : SarEventController<Training, TrainingRoster>
    {
        public override ActionResult Index()
        {
            ViewData["showESAR"] = !string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings["showKCESAR"]);
            return base.Index();
        }

        public ActionResult Home()
        {
            return View();
        }
        
        public ActionResult MyTraining()
        {

            return View();
        }

        #region Training Awards
        [Authorize]
        [HttpGet]
        public ActionResult AwardDetails(Guid? id)
        {
            if (id == null)
            {
                Response.StatusCode = 400;
                return new ContentResult { Content = "No id specified" };
            }

            TrainingAwardView award;
            using (var ctx = GetContext())
            {
                award = (from a in ctx.TrainingAward
                         where a.Id == id
                         select new
                         {
                             Course = new TrainingCourseView
                             {
                                 Id = a.Course.Id,
                                 Title = a.Course.DisplayName
                             },
                             Member = new MemberSummaryRow
                             {
                                 Id = a.Member.Id,
                                 Name = a.Member.LastName + ", " + a.Member.FirstName,
                                 WorkerNumber = a.Member.DEM
                             },
                             Comments = a.metadata,
                             Completed = a.Completed,
                             Expires = a.Expiry,
                             Source = "rule",
                             ReferenceId = a.Id
                         }).AsEnumerable().Select(f => new TrainingAwardView
                             {
                                Course = f.Course,
                                Member = f.Member,
                                Comments = f.Comments,
                                Completed = string.Format(GetDateFormat(), f.Completed),
                                Expires = string.Format(GetDateFormat(), f.Expires),
                                Source = f.Source,
                                ReferenceId = f.ReferenceId
                             }).First();
                ViewData["docs"] = (from d in ctx.Documents where d.ReferenceId == award.ReferenceId select new DocumentView { Id = d.Id, Title = d.FileName, Reference = d.ReferenceId, Type = d.Type, Size = d.Size}).ToArray();
            }

            return View(award);
        }
        #endregion

        [Authorize(Roles = "cdb.users")]
        public ActionResult Rules()
        {
            using (KcsarContext ctx = new KcsarContext())
            {
                Dictionary<Guid, TrainingCourse> courses = (from c in ctx.TrainingCourses select c).ToDictionary(x => x.Id);

                List<TrainingRule> rules = (from r in ctx.TrainingRules select r).ToList();
                string text = "Rules for Training equivalencies\n=========================\n[]'s after course indicate howmany months" +
                " the equivalency is good for. 'default' means as long as the resultant course is good for.\n" +
                "Mission equivalency indicated by (<required hours>:<of mission type>:<within # months>), '%' means any mission type\n\n\n";
                List<string> lines = new List<string>();
                foreach (TrainingRule rule in rules)
                {
                    string line = "";
                    string[] fields = rule.RuleText.Split('>');
                    if (!fields[0].StartsWith("Mission"))
                    {
                        Guid?[] sourceCourses = fields[0].Split('+').Select(f => f.ToGuid()).ToArray();

                        if (sourceCourses.Any(f => f == null))
                        {
                            line += "Unknown rule type: " + rule.Id + "\n";
                            continue;
                        }

                        line += string.Join(", ", sourceCourses.Select(f => courses.ContainsKey(f.Value) ? courses[f.Value].DisplayName : f.ToString())) + " => ";
                    }
                    else
                    {
                        line += fields[0] + " => ";
                    }

                    IEnumerable<string> results = fields[1].Split('+');

                    string sep = "";
                    foreach (string result in results)
                    {
                        string[] parts = result.Split(':');
                        Guid course = new Guid(parts[0]);

                        if (!courses.ContainsKey(course))
                        {
                            line += "Found bad rule: Adds course with ID" + course.ToString() + "\n";
                            continue;
                        }

                        line += sep + courses[course].DisplayName;
                        sep = ", ";

                        if (parts.Length > 1)
                        {
                            string validFor = string.Empty;
                            if (parts[1] == "default")
                            {
                                validFor = courses[course].ValidMonths.HasValue ? courses[course].ValidMonths.ToString() : "no-expire";
                            }
                            else
                            {
                                validFor = parts[1];
                            }
                            line += "[" + validFor + "]";
                        }
                    }
                    lines.Add(line);
                }
                return new ContentResult { Content = text + string.Join("\n\n", lines.OrderBy(f => f).ToArray()), ContentType = "text/plain" };
            }
        }

        [Authorize(Roles = "cdb.users")]
        public ActionResult CourseList(Guid? unit, int? recent, int? upcoming, bool? filter)
        {
            ViewData["PageTitle"] = "KCSARA :: Course List";
            ViewData["Message"] = "Training Courses";

            ViewData["recent"] = recent = recent ?? 3;
            ViewData["upcoming"] = upcoming = upcoming ?? 3;

            ViewData["filter"] = (filter = filter ?? true);

            ViewData["unit"] = UnitsController.GetUnitSelectList(context, unit);
            ViewData["unitFilter"] = unit;

            var courses = (from c in context.TrainingCourses select c).ToDictionary(x => x.Id);

            //var model = from s in context.GetTrainingExpirationsSummary(recent, upcoming, unit)
            //            select new TrainingCourseSummary { Course = courses[s.CourseId], CurrentCount = s.Good, RecentCount = s.Recent, UpcomingCount = s.Almost, FarExpiredCount = s.Expired };
            var model = from s in context.TrainingCourses select new TrainingCourseSummary { Course = s, CurrentCount = 0, RecentCount = 0, UpcomingCount = 0, FarExpiredCount = 0 };


            if ((bool)ViewData["filter"])
            {
                model = model.Where(f => f.Course.WacRequired > 0 || f.Course.ShowOnCard);
            }

            return View(model);
        }

        [Authorize]
        public ActionResult CourseHours(Guid id, DateTime? begin, DateTime? end)
        {
            DateTime e = end ?? DateTime.Now;
            DateTime b = begin ?? e.AddYears(-1);

            TrainingCourseHoursView model = null;
            using (var ctx = GetContext())
            {
                model = (from c in ctx.TrainingCourses where c.Id == id select new TrainingCourseHoursView { Begin = b, End = e, CourseId = id, CourseName = c.DisplayName }).SingleOrDefault();
            }

            if (model == null)
            {
                return new ContentResult { Content = "Course not found" };
            }

            return View(model);
        }

        [HttpPost]
        public ActionResult GetCourseHours(Guid id, DateTime? begin, DateTime? end)
        {
            if (!User.IsInRole("cdb.users")) return GetLoginError();

            DateTime e = end ?? DateTime.Now;
            DateTime b = begin ?? e.AddYears(-1);

            Dictionary<Guid, MemberRosterRow> memberHours = new Dictionary<Guid, MemberRosterRow>();
            using (var ctx = GetContext())
            {
                var traineeQuery = (from tr in ctx.TrainingRosters.Include("TrainingAwards.Member").Include("TrainingAwards.Course") where tr.TrainingAwards.Count > 0 && tr.TimeIn >= b && tr.TimeIn < e select tr).ToArray();
                var traineeAwards = traineeQuery.SelectMany(f => f.TrainingAwards).GroupBy(f => new { P = f.Member, Course = f.Course })
                    .Select(f => new { Person = f.Key.P, Course = f.Key.Course, Hours = f.Sum(g => g.Roster.Hours), Count = f.Count() });

                var rules = ctx.TrainingRules.ToArray();


                foreach (var award in traineeAwards)
                {
                    bool doAward = false;
                    if (award.Course.Id == id)
                    {
                        doAward = true;
                    }
                    else
                    {
                        List<Guid> trickleDowns = new List<Guid>(new[] { award.Course.Id });
                        int trickleCount = trickleDowns.Count - 1;
                        while (trickleCount != trickleDowns.Count)
                        {
                            trickleCount = trickleDowns.Count;

                            foreach (TrainingRule rule in rules)
                            {
                                if (trickleDowns.Any(f => rule.RuleText.StartsWith(f.ToString() + ">", StringComparison.OrdinalIgnoreCase)))
                                {
                                    foreach (Guid newCourse in rule.RuleText.Split('>')[1].Split('+').Select(f => new Guid(f.Split(':')[0])))
                                    {
                                        if (!trickleDowns.Contains(newCourse)) trickleDowns.Add(newCourse);
                                    }
                                }
                            }
                        }
                        doAward = trickleDowns.Any(f => f == id);
                    }

                    if (doAward == true)
                    {
                        if (!memberHours.ContainsKey(award.Person.Id))
                        {
                            memberHours.Add(award.Person.Id, new MemberRosterRow
                            {
                                Person = new MemberSummaryRow { Id = award.Person.Id, Name = award.Person.ReverseName, WorkerNumber = award.Person.DEM },
                                Count = 0,
                                Hours = 0
                            });
                        }
                        memberHours[award.Person.Id].Count += award.Count;
                        memberHours[award.Person.Id].Hours += award.Hours ?? 0.0;
                    }
                }


                //var query = (from tr in ctx.TrainingRosters.Include("Person") where tr.TrainingAwards.Any(f => f.Course.Id == id) && tr.TimeIn > b && tr.TimeIn < e select tr).ToArray();
                //rows = query.GroupBy(f => new { P = f.Person }).Select(f => new MemberRosterRow
                //{
                //    Person = new MemberSummaryRow
                //    {
                //        Id = f.Key.P.Id,
                //        Name = f.Key.P.ReverseName,
                //        WorkerNumber = f.Key.P.DEM
                //    },
                //    Count = f.Count(),
                //    Hours = f.Sum(g => g.Hours)
                //}).OrderByDescending(f => f.Hours).ThenBy(f => f.Person.Name).ToList();
            }
            return Data(memberHours.Values.OrderByDescending(f => f.Hours).ThenBy(f => f.Person.Name));
        }

       

        #region SarEventController base class

        protected override string EventType
        {
            get { return "Training"; }
        }

        protected override bool CanDoAction(SarEventActions action, object context)
        {
            return User.IsInRole("cdb.trainingeditors");
        }

        #endregion

     

        #region Course
        [AcceptVerbs("GET")]
        [Authorize(Roles = "cdb.admins")]
        public ActionResult CreateCourse()
        {
            ViewData["PageTitle"] = "New Training Course";

            TrainingCourse c = new TrainingCourse();
            //UnitMembership s = new UnitMembership();
            //s.Person = (from p in context.Members where p.Id == personId select p).First();
            //s.Activated = DateTime.Today;

            //Session.Add("NewMembershipGuid", s.Id);
            //ViewData["NewMembershipGuid"] = Session["NewMembershipGuid"];

            return InternalEditCourse(c);
        }

        [AcceptVerbs(HttpVerbs.Post)]
        [Authorize(Roles = "cdb.admins")]
        public ActionResult CreateCourse(FormCollection fields)
        {
            //if (Session["NewMembershipGuid"] != null && Session["NewMembershipGuid"].ToString() != fields["NewMembershipGuid"])
            //{
            //    throw new InvalidOperationException("Invalid operation. Are you trying to re-create a status change?");
            //}
            //Session.Remove("NewMembershipGuid");

            //ViewData["PageTitle"] = "New Unit Membership";

            //UnitMembership um = new UnitMembership();
            //um.Person = (from p in context.Members where p.Id == personId select p).First();
            //context.AddToUnitMemberships(um);
            //return InternalSaveMembership(um, fields);
            return null;
        }


        [AcceptVerbs("GET")]
        [Authorize(Roles = "cdb.admins")]
        public ActionResult EditCourse(Guid id)
        {
            TrainingCourse c = GetCourse(id);
            ViewData["HideFrame"] = true;

            return InternalEditCourse(c);
        }

        private ActionResult InternalEditCourse(TrainingCourse um)
        {
            KcsarContext context = new KcsarContext();
            //SarUnit[] units = (from u in context.Units orderby u.DisplayName select u).ToArray();

            //Guid selectedUnit = (um.Unit != null) ? um.Unit.Id : Guid.Empty;

            //// MVC RC BUG - Have to store list in a unique key in order for SelectedItem to work
            //ViewData["Unit"] = new SelectList(units, "Id", "DisplayName", selectedUnit);

            //if (selectedUnit == Guid.Empty && units.Length > 0)
            //{
            //    selectedUnit = units.First().Id;
            //}

            //ViewData["Status"] = new SelectList(
            //        (from s in context.UnitStatusTypes.Include("Unit") where s.Unit.Id == selectedUnit orderby s.StatusName select s).ToArray(),
            //        "Id",
            //        "StatusName",
            //        (um.Status != null) ? (Guid?)um.Status.Id : null);

            return View("EditCourse", um);
        }

        [AcceptVerbs("POST")]
        [Authorize(Roles = "cdb.admins")]
        public ActionResult EditCourse(Guid id, FormCollection fields)
        {
            TrainingCourse c = GetCourse(id);
            return InternalSaveCourse(c, fields);
        }


        private ActionResult InternalSaveCourse(TrainingCourse c, FormCollection fields)
        {
            try
            {
                TryUpdateModel(c, new string[] { "DisplayName", "FullName", "OfferedFrom", "OfferedTo", "ValidMonths", "ShowOnCard", "WacRequired" });

                if (ModelState.IsValid)
                {
                    context.SaveChanges();
                    TempData["message"] = "Saved";
                    return RedirectToAction("ClosePopup");
                }
            }
            catch (RuleViolationsException ex)
            {
                this.CollectRuleViolations(ex, fields);
            }
            return InternalEditCourse(c);
        }

        [AcceptVerbs(HttpVerbs.Get)]
        [Authorize(Roles = "cdb.admins")]
        public ActionResult DeleteCourse(Guid id)
        {
            return View(GetCourse(id));
        }

        [AcceptVerbs(HttpVerbs.Post)]
        [Authorize(Roles = "cdb.admins")]
        public ActionResult DeleteCourse(Guid id, FormCollection fields)
        {
            TrainingCourse c = GetCourse(id);
            context.TrainingCourses.Remove(c);
            context.SaveChanges();

            return RedirectToAction("ClosePopup");
        }

        private TrainingCourse GetCourse(Guid id)
        {
            return GetCourse(context.TrainingCourses, id);
        }

        private TrainingCourse GetCourse(IEnumerable<TrainingCourse> context, Guid id)
        {
            List<TrainingCourse> courses = (from m in context where m.Id == id select m).ToList();
            if (courses.Count != 1)
            {
                throw new ApplicationException(string.Format("{0} training courses found with ID = {1}", courses.Count, id.ToString()));
            }

            TrainingCourse course = courses[0];
            return course;
        }

        #endregion

    }

    public class TrainingCourseSummary
    {
        public TrainingCourse Course { get; set; }
        public int CurrentCount { get; set; }
        public int UpcomingCount { get; set; }
        public int RecentCount { get; set; }
        public int FarExpiredCount { get; set; }
    }

}
