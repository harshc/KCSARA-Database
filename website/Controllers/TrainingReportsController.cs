namespace Kcsara.Database.Web.Controllers
{
    using Kcsar.Database;
    using Kcsar.Database.Model;
    using Kcsara.Database.Web.Model;
    using System;
    using System.Collections.Generic;
    using System.Configuration;
    using System.Data.Entity.Infrastructure;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Web.Mvc;
    using System.Xml;

    public partial class TrainingController : SarEventController<Training, TrainingRoster>
    {
        [Authorize(Roles = "cdb.users")]
        public ActionResult ReportStatus()
        {
            IEnumerable<EventReportStatusView> model;
            using (var ctx = GetContext())
            {
                IQueryable<Training> source = ctx.Trainings;

                var docCount = (from d in ctx.Documents group d by d.ReferenceId into g select new { Id = g.Key, Count = g.Count() }).ToDictionary(f => f.Id, f => f.Count);

                model = (from m in source
                         select new EventReportStatusView
                         {
                             Id = m.Id,
                             Title = m.Title,
                             Number = m.StateNumber,
                             StartTime = m.StartTime,
                             Persons = m.Roster.Select(f => f.Person.Id).Distinct().Count()
                         }).OrderByDescending(f => f.StartTime).ToArray();

                foreach (var r in model)
                {
                    int count;
                    if (docCount.TryGetValue(r.Id, out count))
                    {
                        r.DocumentCount = count;
                    }
                }
            }

            return View(model);
        }

        protected override void DeleteDependentObjects(Training evt)
        {
            base.DeleteDependentObjects(evt);
            foreach (var roster in context.TrainingRosters.Include("TrainingAwards").Where(f => f.Training.Id == evt.Id))
            {
                List<TrainingAward> copy = new List<TrainingAward>(roster.TrainingAwards);
                foreach (var award in copy)
                {
                    context.TrainingAward.Remove(award);
                }
                context.TrainingRosters.Remove(roster);
            }
        }

        public static IList<TrainingCourse> GetCoreCompetencyCourses(IKcsarContext context)
        {
            var courses = new[] {
                "Clues",
                "Crime",
                "FA",
                "Fitness",
                "GPS.P", "GPS.W",
                "Helicopter",
                "Legal",
                "Management",
                "Nav.P", "Nav.W",
                "Radio",
                "Rescue.P", "Rescue.W",
                "Safety.P", "Safety.W",
                "Search.P", "Search.W",
                "Survival.P", "Survival.W"
            }.Select(f => "Core/" + f).ToArray();

            return (from c in context.TrainingCourses where courses.Contains(c.DisplayName) select c).OrderBy(f => f.DisplayName).ToList();
        }

        [Authorize(Roles = "cdb.users")]
        public ActionResult CoreCompReport(Guid? id)
        {
            IQueryable<UnitMembership> memberships = context.UnitMemberships.Include("Person.ComputedAwards.Course").Include("Status");
            string unitShort = ConfigurationManager.AppSettings["dbNameShort"];
            string unitLong = Strings.GroupName;
            if (id.HasValue)
            {
                memberships = memberships.Where(um => um.Unit.Id == id.Value);
                SarUnit sarUnit = (from u in context.Units where u.Id == id.Value select u).First();
                unitShort = sarUnit.DisplayName;
                unitLong = sarUnit.LongName;

            }
            memberships = memberships.Where(um => um.EndTime == null && um.Status.IsActive);
            var members = memberships.Select(f => f.Person).Distinct().OrderBy(f => f.LastName).ThenBy(f => f.FirstName);

            var courses = GetCoreCompetencyCourses(context);

            var file = ExcelService.Create(ExcelFileType.XLS);
            var sheet = file.CreateSheet(unitShort);

            var headers = new[] { "DEM #", "Last Name", "First Name", "Field Type", "Good Until" }.Union(courses.Select(f => f.DisplayName)).ToArray();
            for (int i = 0; i < headers.Length; i++)
            {
                sheet.CellAt(0, i).SetValue(headers[i]);
                sheet.CellAt(0, i).SetBold(true);
            }

            int row = 1;
            foreach (var member in members)
            {
                sheet.CellAt(row, 0).SetValue(member.DEM);
                sheet.CellAt(row, 1).SetValue(member.LastName);
                sheet.CellAt(row, 2).SetValue(member.FirstName);
                sheet.CellAt(row, 3).SetValue(member.WacLevel.ToString());

                int goodColumn = 4;
                int col = goodColumn + 1;

                DateTime goodUntil = DateTime.MaxValue;
                int coursesCount = 0;

                for (int i = 0; i < courses.Count; i++)
                {
                    var match = member.ComputedAwards.SingleOrDefault(f => f.Course.Id == courses[i].Id);
                    if (match != null)
                    {
                        if (match.Expiry < goodUntil) goodUntil = match.Expiry.Value;
                        coursesCount++;
                        sheet.CellAt(row, col + i).SetValue(match.Expiry.Value.ToString("yyyy-MM-dd"));
                    }
                }
                sheet.CellAt(row, goodColumn).SetValue(courses.Count == coursesCount ? goodUntil.ToString("yyyy-MM-dd") : "");
                row++;
            }


            MemoryStream ms = new MemoryStream();
            file.Save(ms);
            ms.Seek(0, SeekOrigin.Begin);
            return this.File(ms, "application/vnd.ms-excel", string.Format("{0}-corecomp-{1:yyyy-MM-dd}.xls", unitShort, DateTime.Today));
        }

        [Authorize(Roles = "cdb.users")]
        public ActionResult RequiredTrainingReport()
        {
            DateTime perfStart = DateTime.Now;
            Dictionary<Member, CompositeTrainingStatus> model = new Dictionary<Member, CompositeTrainingStatus>();
            IDictionary<SarUnit, IList<Member>> unitLookup = new Dictionary<SarUnit, IList<Member>>();

            var members = (from um in context.UnitMemberships.Include("Person").Include("Status") where um.EndTime == null select um.Person).Distinct().OrderBy(x => x.LastName + ", " + x.FirstName);

            var courses = (from c in context.TrainingCourses where c.WacRequired > 0 select c).OrderBy(x => x.DisplayName).ToList();

            var expirations = (from e in context.ComputedTrainingAwards where e.Course.WacRequired > 0 orderby e.Member.Id select new { memberId = e.Member.Id, Award = e });

            ViewData["courseList"] = (from c in courses select c.DisplayName).ToArray();
            ViewData["Title"] = "Training Expiration Report";
            ViewData["UnitTable"] = unitLookup;

            foreach (Member m in members)
            {
                UnitMembership[] units = m.GetActiveUnits();
                if (units.Length == 0)
                {
                    continue;
                }

                foreach (var u in m.GetActiveUnits())
                {
                    //if (!u.UnitReference.IsLoaded)
                    //{
                    //    u.UnitReference.Load();
                    //}
                    if (!unitLookup.ContainsKey(u.Unit))
                    {
                        unitLookup.Add(u.Unit, new List<Member>());
                    }
                    unitLookup[u.Unit].Add(m);
                }

                model.Add(m, CompositeTrainingStatus.Compute(m, (from e in expirations where e.memberId == m.Id select e.Award), courses, DateTime.Now));
            }
            ViewData["perf"] = (DateTime.Now - perfStart).TotalSeconds;
            return View(model);
        }

        [HttpPost]
        public DataActionResult GetRequiredTraining()
        {
            if (!Permissions.IsUserOrLocal(Request)) return GetLoginError();

            object model;
            using (var ctx = GetContext())
            {
                model = (from c in ctx.TrainingCourses where c.WacRequired > 0 select new TrainingCourseView { Id = c.Id, Required = c.WacRequired, Title = c.DisplayName }).OrderBy(f => f.Title).ToList();
            }
            return Data(model);
        }

        [HttpPost]
        public DataActionResult GetMemberExpirations(Guid id)
        {
            if (!Permissions.IsUser && !Permissions.IsSelf(id)) return GetLoginError();

            CompositeExpirationView model;
            using (var ctx = GetContext())
            {
                var courses = (from c in ctx.TrainingCourses where c.WacRequired > 0 select c).OrderBy(x => x.DisplayName).ToDictionary(f => f.Id, f => f);

                Member m = ctx.Members.Include("ComputedAwards.Course").FirstOrDefault(f => f.Id == id);

                CompositeTrainingStatus stats = CompositeTrainingStatus.Compute(m, courses.Values, DateTime.Now);

                model = new CompositeExpirationView
                {
                    Goodness = stats.IsGood,
                    Expirations = stats.Expirations.Select(f => new TrainingExpirationView
                    {
                        Completed = string.Format(GetDateFormat(), f.Value.Completed),
                        Course = new TrainingCourseView
                        {
                            Id = f.Value.CourseId,
                            Required = courses[f.Value.CourseId].WacRequired,
                            Title = courses[f.Value.CourseId].DisplayName
                        },
                        Expires = string.Format(GetDateFormat(), f.Value.Expires),
                        Status = f.Value.Status.ToString(),
                        ExpiryText = f.Value.ToString()
                    }).OrderBy(f => f.Course.Title).ToArray()
                };


                return Data(model);
            }
        }

        public ActionResult GetRequiredTrainingExpirations(Guid? id)
        {
            if (!Permissions.IsUserOrLocal(Request)) return GetLoginError();

            XmlDocument doc = new XmlDocument();
            XmlElement root = doc.CreateElement("Expirations");
            root.SetAttribute("generated", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"));
            doc.AppendChild(root);

            using (var ctx = GetContext())
            {
                var courses = (from c in ctx.TrainingCourses where c.WacRequired > 0 select c).OrderBy(x => x.DisplayName).ToList();

                var source = ctx.GetActiveMembers(id, DateTime.Now, "ComputedAwards.Course");

                foreach (Member m in source)
                {
                    XmlElement person = doc.CreateElement("Member");
                    person.SetAttribute("id", m.Id.ToString());
                    person.SetAttribute("last", m.LastName);
                    person.SetAttribute("first", m.FirstName);
                    person.SetAttribute("dem", m.DEM);
                    person.SetAttribute("card", m.WacLevel.ToString());
                    root.AppendChild(person);
                    CompositeTrainingStatus stats = CompositeTrainingStatus.Compute(m, courses, DateTime.Now);
                    person.SetAttribute("current", stats.IsGood ? "yes" : "no");
                    foreach (TrainingCourse c in courses)
                    {
                        person.SetAttribute(Regex.Replace(c.DisplayName, "[^a-zA-Z0-9]", ""), stats.Expirations[c.Id].ToString().ToXmlAttr());
                    }
                }


                return new ContentResult { Content = doc.OuterXml, ContentType = "application/xml" };
            }
        }

        [Authorize(Roles = "cdb.admins")]
        public ActionResult RecalculateAwards(Guid? id)
        {
            if (id.HasValue)
            {
                context.RecalculateTrainingAwards(id.Value);
            }
            else
            {
                context.RecalculateTrainingAwards();
            }
            context.SaveChanges();
            return new ContentResult { Content = "Done" };
        }

        [Authorize(Roles = "cdb.users")]
        public ActionResult Current(Guid id, Guid? unit, bool? expired)
        {
            ViewData["PageTitle"] = "KCSARA :: Training Course";
            ViewData["Course"] = (from c in context.TrainingCourses where c.Id == id select c).First();

            ViewData["unit"] = UnitsController.GetUnitSelectList(context, unit);
            ViewData["expired"] = (expired = expired ?? false);

            // I'm sure there's a better way to do this, but I'll have to come back to it when I become more of a Linq/Entities/SQL guru.
            // What's here now...
            // SELECT everyone that's taken this course, sorted by name and date. This set will have multiple rows for a person
            // that has taken the course more than once. They should be sorted so that the most recent of these rows comes first.
            // Run through the list, and pull out the earlier records for this person and course.

            Guid lastId = Guid.Empty;
            IQueryable<ComputedTrainingAward> src = context.ComputedTrainingAwards.Include("Member").Include("Course").Include("Member.Memberships.Unit").Include("Member.Memberships.Status");

            var model = (from ta in src where ta.Course.Id == id select ta);
            if (!(bool)ViewData["expired"])
            {
                model = model.Where(ta => (ta.Expiry == null || ta.Expiry >= DateTime.Today));
            }
            List<ComputedTrainingAward> awards = model.OrderBy(ta => ta.Member.LastName).ThenBy(f => f.Member.FirstName).ThenBy(f => f.Member.Id).ThenByDescending(f => f.Expiry).ToList();

            if (unit.HasValue)
            {
                awards = awards.Where(f => f.Member.GetActiveUnits().Where(g => g.Unit.Id == unit.Value).Count() > 0).ToList();
            }
            else
            {
                awards = awards.Where(f => f.Member.GetActiveUnits().Count() > 0).ToList();
            }
            return View(awards);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="id">Course ID</param>
        /// <returns></returns>
        [Authorize(Roles = "cdb.users")]
        public ActionResult Eligible(Guid id)
        {
            TrainingCourse course = (from c in context.TrainingCourses where c.Id == id select c).First();

            ViewData["Course"] = course;

            IEnumerable<Member> memberList = course.GetEligibleMembers(
                context.Members.Include("ComputedAwards").Include("ComputedAwards.Course").Include("ContactNumbers").OrderBy(f => f.LastName + f.FirstName),
                false);

            List<string> emailList = new List<string>();
            foreach (Member m in memberList)
            {
                var emails = (from c in m.ContactNumbers where c.Type == "email" select c.Value);
                foreach (string email in emails)
                {
                    string text = string.Format("{0} <{1}>", m.FullName, email);
                    if (!emailList.Contains(text))
                    {
                        emailList.Add(text);
                    }
                }
            }
            ViewData["emails"] = emailList;

            return View(memberList);

        }

        protected override TrainingRoster AddNewRow(Guid id)
        {
            TrainingRoster row = new TrainingRoster { Id = id };
            context.TrainingRosters.Add(row);
            return row;
        }

        protected override void AddEventToContext(Training newEvent)
        {
            context.Trainings.Add(newEvent);
        }

        private List<Guid> dirtyAwardMembers = new List<Guid>();

        protected override void OnProcessingRosterInput(TrainingRoster row, FormCollection fields)
        {
            base.OnProcessingRosterInput(row, fields);

            string coursesKey = "courses_" + row.Id.ToString();
            string loweredCourses = (fields[coursesKey] ?? "").ToLower();

            if (!string.IsNullOrEmpty(loweredCourses))
            {
                ModelState.SetModelValue(coursesKey, new ValueProviderResult(fields[coursesKey], fields[coursesKey], CultureInfo.CurrentUICulture));
            }

            TrainingAward[] tmp = row.TrainingAwards.ToArray();

            Dictionary<string, TrainingAward> currentAwards = row.TrainingAwards.ToDictionary(f => f.Course.Id.ToString().ToLower(), f => f);
            bool awardsDirty = false;

            foreach (string award in currentAwards.Keys)
            {
                string lowered = award.ToLower();
                if (loweredCourses.Contains(lowered))
                {
                    loweredCourses = loweredCourses.Replace(lowered, "");
                }
                else
                {
                    awardsDirty = true;
                    context.TrainingAward.Remove(currentAwards[award]);
                }
            }

            foreach (string key in loweredCourses.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!row.TimeOut.HasValue)
                {
                    ModelState.AddModelError(coursesKey, "Time out required when awarding courses");
                    return;
                    //ModelState.AddModelError("TimeOut", "Time out is required if course is awarded.");
                    //throw new InvalidOperationException("row's timeout is null");
                }

                if (row.Person == null)
                {
                    return;
                }

                if (!ModelState.IsValid)
                {
                    return;
                }

                TrainingCourse course = GetCourse(new Guid(key));

                TrainingAward newAward = new TrainingAward()
                {
                    Member = row.Person,
                    Roster = row,
                    Completed = row.TimeOut.Value,
                    Course = course,
                    Expiry = course.ValidMonths.HasValue ? row.TimeOut.Value.AddMonths(course.ValidMonths.Value) : (DateTime?)null
                };

                awardsDirty = true;
                row.TrainingAwards.Add(newAward);
            }

            if (awardsDirty && row.Person != null && !dirtyAwardMembers.Contains(row.Person.Id))
            {
                dirtyAwardMembers.Add(row.Person.Id);
            }
        }

        protected override void OnRosterPostProcessing()
        {
            base.OnRosterPostProcessing();
            foreach (Guid memberId in dirtyAwardMembers)
            {
                context.RecalculateTrainingAwards(memberId);
            }
            dirtyAwardMembers.Clear();
            context.SaveChanges();
        }

        protected override void OnDeletingRosterRow(TrainingRoster row)
        {
            base.OnDeletingRosterRow(row);

            Member member = null;

            // Take away any rewards that may have come with this roster row.
            while (row.TrainingAwards.Count > 0)
            {
                context.TrainingAward.Remove(row.TrainingAwards.First());
                member = row.Person;
            }

            // Figure out what this means for the rest of the member's training
            if (member != null)
            {
                context.RecalculateTrainingAwards(member.Id);
            }
        }

        protected override ActionResult InternalEdit(Training evt)
        {
            var courses = (from c in context.TrainingCourses orderby c.DisplayName select c);

            Dictionary<string, string> courseList = courses.ToDictionary(f => f.Id.ToString(), f => f.DisplayName);
            string[] offered = evt.OfferedCourses.Select(f => f.Id.ToString()).ToArray();

            ViewData["OfferedCourses"] = new MultiSelectList(courseList, "Key", "Value", offered);

            return base.InternalEdit(evt);
        }

        protected override void OnProcessingEventModel(Training evt, FormCollection fields)
        {
            base.OnProcessingEventModel(evt, fields);

            evt.OfferedCourses.Clear();

            if (fields["OfferedCourses"] != null)
            {
                foreach (TrainingCourse course in (from c in context.TrainingCourses select c))
                {
                    if (fields["OfferedCourses"].ToLower().Contains(course.Id.ToString()))
                    {
                        evt.OfferedCourses.Add(course);
                    }
                }
            }

        }

        [Authorize(Roles = "cdb.users")]
        public ContentResult EligibleEmails(Guid? unit, Guid eligibleFor, Guid[] haveFinished)
        {
            //using (var ctx = this.GetContext())
            //{
            //    var persons = (from m in ctx.GetActiveMembers(unit, DateTime.Now, "ComputedAwards", "Memberships.Status")
            //                   where m.BackgroundDate != null
            //                   && m.Memberships.Any(f => f.Unit.DisplayName == "ESAR" && f.Status.StatusName == "trainee" && f.EndTime == null)
            //                   && m.ComputedAwards.Any(f => f.Course.Id == haveFinished)
            //                   && m.ComputedAwards.All(f => f.Course.Id != eligibleFor)
            //                   select m);

            //    //var mails = (from m in ctx.GetActiveMembers(unit,DateTime.Now,"ContactNumbers") join ca in ctx.ComputedTrainingAwards on m.Id equals ca.Member.Id
            //    //             where m.BackgroundDate != null
            //    //              && m.Memberships.Any(f => f.Unit.DisplayName == "ESAR" && f.Status.StatusName == "trainee" && f.EndTime == null)
            //    //              && m.ComputedAwards.Any(f => f.Course.Id == haveFinished)
            //    //              && m.ComputedAwards.All(f => f.Course.Id != eligibleFor)
            //    //             select m).SelectMany(f => f.ContactNumbers);

            //    //var mails = ctx.UnitMemberships.Include("Person.ContactNumbers").Include("Unit").Include("Status").Where(ctx.GetActiveMembershipFilter(unit, DateTime.Now))
            //    //    .Where(f => f.Status.StatusName == "trainee")
            //    //    .SelectMany(f => f.Person.ContactNumbers)
            //    //        .Where(f => f.Type == "email")
            //    //        .Select(f => string.Format("{0} <{1}>", f.Person.FullName, f.Value));

            //    //return new ContentResult { Content = string.Join("; ", mails), ContentType = "text/plain" };
            //    // ctx.GetActiveMembers(unit, DateTime.Now).SelectMany(f => f.ComputedAwards, f => new { f.);

            //    //var mails = (from m in ctx.GetActiveMembers(unit, DateTime.Now, "Contacts", "ComputedAwards")
            //    //             from ca in ctx.ComputedTrainingAwards on ca.

            //    //return new ContentResult { Content = string.Join("\n", persons.AsEnumerable().Select(f => f.ReverseName)), ContentType = "text/plain" };
            //    //return new ContentResult { Content = string.Join("; ", mails.AsEnumerable().Select(f => string.Format("{0} <{1}>", f.Person.FullName, f.Value))), ContentType = "text/plain" };
            //}

            List<Guid> allCourses = new List<Guid>(haveFinished);
            allCourses.Add(eligibleFor);

            using (var ctx = this.GetContext())
            {
                var mails = ((IObjectContextAdapter)ctx).ObjectContext.ExecuteStoreQuery<string>(string.Format(@"SELECT p.lastname + ', ' + p.firstname + ' <' + pc.value + '>'
FROM ComputedTrainingAwards cta LEFT JOIN ComputedTrainingAwards cta2
 ON cta.member_id=cta2.member_id AND cta2.course_id='{0}' AND ISNULL(cta2.Expiry, '9999-12-31') >= GETDATE()
 JOIN Members p ON p.id=cta.member_id
 JOIN PersonContacts pc ON pc.person_id=p.id AND pc.type='email'
WHERE cta2.member_id is null and (cta.Expiry IS NULL OR cta.Expiry >= GETDATE()) AND cta.course_id IN ('{1}')
GROUP BY cta.member_id,lastname,firstname,pc.value
HAVING COUNT(cta.member_id) = {2}
ORDER BY lastname,firstname", eligibleFor, string.Join("','", haveFinished.Select(f => f.ToString())), haveFinished.Length));

                ////var courses = ctx.TrainingAward.Where(f => allCourses.Contains(f.Course.Id)).ToList();



                //var query = ctx.UnitMemberships.Include("Person.ContactNumbers").Include("Person.ComputedAwards.Course").Include("Unit").Include("Status").Where(ctx.GetActiveMembershipFilter(unit, DateTime.Now))
                //    .Where(f => f.Status.StatusName == "trainee"
                //                && f.Person.BackgroundDate.HasValue
                //                && f.Person.ComputedAwards.All(g => g.Course.Id != eligibleFor));

                //DateTime now = DateTime.Today;
                //foreach (Guid prereq in haveFinished)
                //{
                //    query = query.Where(f => f.Person.ComputedAwards.Any(g => g.Course.Id == prereq && (g.Expiry == null || g.Expiry > now)));
                //}

                //var mails = query.SelectMany(f => f.Person.ContactNumbers)
                //        .Where(f => f.Type == "email")
                //        .OrderBy(f => f.Person.ReverseName)
                //        .Select(f => string.Format("{0} <{1}>", f.Person.FullName, f.Value));

                return new ContentResult { Content = string.Join("; ", mails), ContentType = "text/plain" };
            }
        }


        [Authorize(Roles = "cdb.users")]
        public FileStreamResult IstTrainingReport(Guid? id, DateTime? date)
        {
            id = id ?? new Guid("c118ce30-cd28-4635-ba3d-adf7c21358e2");

            DateTime today = date ?? DateTime.Today;
            // Take current month and subtract 1 to move to Jan = 0 counting system
            // Take away one more so that reports run during the first month of a new quarter report on last quarter.
            // Then, convert -1 to 12 with +12,%12
            // Divide by 3 months to get the quarter
            int quarter = ((today.Month + 10) % 12) / 3;
            DateTime quarterStart = new DateTime(today.AddMonths(-1).Year, 1, 1).AddMonths(quarter * 3);
            DateTime quarterStop = quarterStart.AddMonths(3);


            ExcelFile file = ExcelService.Create(ExcelFileType.XLS);
            ExcelSheet sheet = file.CreateSheet("IST Members");

            var members = context.GetActiveMembers(id, today, "ContactNumbers", "Memberships").OrderBy(f => f.LastName).ThenBy(f => f.FirstName);

            var istCourses = new[] { "ICS-300", "ICS-400", "ICS-200", "ICS-800" };
            var courses = context.TrainingCourses.Where(f => istCourses.Contains(f.DisplayName) || f.WacRequired > 0).OrderBy(f => f.DisplayName);

            sheet.CellAt(0, 0).SetValue(today.ToString());
            sheet.CellAt(0, 0).SetBold(true);

            var headers = new[] { "Last Name", "First Name", "Ham Call", "Card Type", "Status", "Missing Training", "Mission Ready", string.Format("Q{0} Missions", quarter + 1), string.Format("Q{0} Meetings", quarter + 1) };
            for (int i = 0; i < headers.Length; i++)
            {
                sheet.CellAt(2, i).SetValue(headers[i]);
                sheet.CellAt(2, i).SetBold(true);
            }

            int row = 3;
            foreach (var member in members)
            {
                sheet.CellAt(row, 0).SetValue(member.LastName);
                sheet.CellAt(row, 1).SetValue(member.FirstName);
                sheet.CellAt(row, 2).SetValue(member.ContactNumbers.Where(f => f.Type == "hamcall").Select(f => f.Value).FirstOrDefault());
                sheet.CellAt(row, 3).SetValue(member.WacLevel.ToString());
                sheet.CellAt(row, 4).SetValue(member.Memberships.Where(f => f.Unit.Id == id.Value && f.EndTime == null).Select(f => f.Status.StatusName).FirstOrDefault());


                var expires = CompositeTrainingStatus.Compute(member, courses, today);

                List<string> missingCourses = new List<string>();
                foreach (var course in courses)
                {
                    if (!expires.Expirations[course.Id].Completed.HasValue) missingCourses.Add(course.DisplayName);
                }

                sheet.CellAt(row, 5).SetValue(string.Join(", ", missingCourses));



                sheet.CellAt(row, 7).SetValue(member.MissionRosters.Where(f => f.Unit.Id == id && f.TimeIn >= quarterStart && f.TimeIn < quarterStop).Select(f => f.Mission.Id).Distinct().Count());
                var trainingRosters = member.TrainingRosters.Where(f => f.TimeIn >= quarterStart && f.TimeIn < quarterStop).ToList();
                sheet.CellAt(row, 8).SetValue(trainingRosters.Where(f => Regex.IsMatch(f.Training.Title, "IST .*Meeting.*", RegexOptions.IgnoreCase)).Select(f => f.Training.Id).Distinct().Count());

                row++;
            }


            string filename = string.Format("ist-training-{0:yyMMdd}.xls", today);

            MemoryStream ms = new MemoryStream();
            file.Save(ms);
            ms.Seek(0, SeekOrigin.Begin);
            return this.File(ms, "application/vnd.ms-excel", filename);
        }


        [Authorize(Roles = "cdb.users")]
        public FileStreamResult EsarTrainingReport()
        {
            DateTime today = DateTime.Today;

            ExcelFile xl;
            using (FileStream fs = new FileStream(Server.MapPath(Url.Content("~/Content/esartraining-template.xls")), FileMode.Open, FileAccess.Read))
            {
                xl = ExcelService.Read(fs, ExcelFileType.XLS);
            }

            ExcelSheet ws = xl.GetSheet(0);

            int row = 0;

            using (var ctx = GetContext())
            {
                var courseNames = new List<string>(new[] { "Course A", "Course B", "Course C", "NIMS I-100", "NIMS I-700", "Course I", "Course II", "Course III" });
                var courses = (from tc in ctx.TrainingCourses where (tc.Unit.DisplayName == "ESAR" && tc.Categories.Contains("basic")) || tc.DisplayName.StartsWith("NIMS ") orderby tc.DisplayName select tc).ToArray()
                    .Where(f => courseNames.Contains(f.DisplayName)).OrderBy(f => courseNames.IndexOf(f.DisplayName)).ToList();


                using (SheetAutoFitWrapper wrap = new SheetAutoFitWrapper(xl, ws))
                {

                    foreach (var traineeRow in (from um in ctx.UnitMemberships.Include("Person.ComputedAwards.Course").Include("Person.ContactNumbers")
                                                where um.Unit.DisplayName == "ESAR" && um.Status.StatusName == "trainee" && um.EndTime == null
                                                orderby um.Person.FirstName
                                                orderby um.Person.LastName
                                                select new { p = um.Person, a = um.Person.ComputedAwards, n = um.Person.ContactNumbers }))
                    {
                        row++;
                        Member trainee = traineeRow.p;
                        //trainee.ContactNumbers.Add(traineeRow.n);
                        //trainee.ComputedAwards.Add(traineeRow.
                        //trainee.ContactNumbers.Attach(traineeRow.n);
                        //trainee.ComputedAwards.Attach(traineeRow.a);

                        int col = 0;
                        wrap.SetCellValue(trainee.LastName, row, col++);
                        wrap.SetCellValue(trainee.FirstName, row, col++);
                        wrap.SetCellValue(trainee.BirthDate.HasValue ? ((trainee.BirthDate.Value.AddYears(21) > DateTime.Today) ? "Y" : "A") : "?", row, col++);
                        wrap.SetCellValue(trainee.InternalGender, row, col++);
                        wrap.SetCellValue(string.Join("\n", trainee.ContactNumbers.Where(f => f.Type.ToLowerInvariant() == "phone" && f.Subtype.ToLowerInvariant() == "home").Select(f => f.Value).ToArray()), row, col++);
                        wrap.SetCellValue(string.Join("\n", trainee.ContactNumbers.Where(f => f.Type.ToLowerInvariant() == "phone" && f.Subtype.ToLowerInvariant() == "cell").Select(f => f.Value).ToArray()), row, col++);
                        wrap.SetCellValue(string.Join("\n", trainee.ContactNumbers.Where(f => f.Type.ToLowerInvariant() == "email").Select(f => f.Value).ToArray()), row, col++);

                        int nextCourseCol = col++;
                        bool foundNext = false;

                        string courseName = string.Format("{0}", wrap.Sheet.CellAt(0, col).StringValue);
                        while (!string.IsNullOrWhiteSpace(courseName))
                        {
                            var record = trainee.ComputedAwards.FirstOrDefault(f => f.Course.DisplayName == courseName && (f.Expiry == null || f.Expiry > today));

                            if (courseName.Equals("Worker App") && trainee.SheriffApp.HasValue)
                            {
                                wrap.SetCellValue("X", row, col);
                            }
                            else if (courseName.Equals("BG Check") && trainee.BackgroundDate.HasValue)
                            {
                                wrap.SetCellValue("X", row, col);
                            }
                            else if (record != null)
                            {
                                wrap.SetCellValue(string.Format("{0:yyyy-MM-dd}", record.Completed), row, col);
                            }
                            else if (foundNext == false)
                            {
                                wrap.SetCellValue(courseName, row, nextCourseCol);
                                foundNext = true;
                            }

                            col++;
                            courseName = string.Format("{0}", wrap.Sheet.CellAt(0, col).StringValue);
                        }

                        if (trainee.PhotoFile != null) wrap.SetCellValue("havePhoto", row, col++);

                        //    wrap.SetCellValue(trainee.SheriffApp.HasValue ? "X" : "", row, col++);
                        //    wrap.SetCellValue(trainee.BackgroundDate.HasValue ? "X" : "", row, col+1);


                        //    bool needNextCourse = false;
                        //    if (!trainee.ComputedAwards.Any(f => f.Course.DisplayName == "Course A" && f.Expiry > today))
                        //    {
                        //        // Hasn't taken Course A this year
                        //        wrap.SetCellValue("Course A", row, col++);
                        //    }
                        //    else if (!trainee.SheriffApp.HasValue)
                        //    {
                        //        wrap.SetCellValue("Waiting App", row, col++);
                        //    }
                        //    else if (!trainee.ComputedAwards.Any(f => f.Course.DisplayName == "Course B" && f.Expiry > today))
                        //    {
                        //        wrap.SetCellValue("Course B", row, col++);
                        //    }
                        //    else if (!trainee.BackgroundDate.HasValue)
                        //    {
                        //        wrap.SetCellValue("Waiting BG", row, col++);
                        //    }
                        //    else
                        //    {
                        //        needNextCourse = true;
                        //        nextCourseCol = col++;
                        //    }

                        //    string nextCourse = string.Empty;
                        //    //DateTime cutoff = new DateTime(DateTime.Today.AddMonths(-5).Year, 6, 1);
                        //    foreach (var course in courses)
                        //    {
                        //        DateTime? completed = trainee.ComputedAwards.Where(f => f.Course != null && f.Course.Id == course.Id && (f.Expiry == null || f.Expiry > today)).Select(f => f.Completed).SingleOrDefault();
                        //            //.Where(f => f.Course != null && f.Course.Id == course.Id && f.Completed > cutoff).OrderBy(f => f.Completed).Select(f => f.Completed).FirstOrDefault();
                        //        if (completed.HasValue)
                        //        {
                        //            while (!string.IsNullOrWhiteSpace(string.Format("{0}", wrap.Sheet.Cells[1, col].Value))
                        //                && string.Format("{0}", wrap.Sheet.Cells[1, col].Value).Replace("ICS ", "NIMS I-") != course.DisplayName)
                        //            {
                        //                wrap.Sheet.Cells[row, col].Value = wrap.Sheet.Cells[1, col].Value;
                        //                col++;
                        //            }
                        //            wrap.Sheet.Cells[row, col].Value = completed.Value.Date;
                        //            wrap.Sheet.Cells[row, col].Style.NumberFormat = "yyyy-mm-dd";
                        //        }
                        //        else if (string.IsNullOrWhiteSpace(nextCourse))
                        //        {
                        //            nextCourse = course.DisplayName;
                        //        }
                        //        col++;
                        //    }
                        //    if (needNextCourse) wrap.SetCellValue(nextCourse, row, nextCourseCol);
                        //    if (trainee.PhotoFile != null) wrap.Sheet.Cells[row, nextCourseCol + courses.Count + 1].Value = "havePhoto";
                        //}
                    }
                    wrap.AutoFit();
                }
            }


            string filename = string.Format("esar-training-{0:yyMMdd}.xls", DateTime.Now);

            MemoryStream ms = new MemoryStream();
            xl.Save(ms);
            ms.Seek(0, SeekOrigin.Begin);
            return this.File(ms, "application/vnd.ms-excel", filename);
        }
        
        protected override void RemoveEvent(Training oldEvent)
        {
            context.Trainings.Remove(oldEvent);
        }

        protected override void RemoveRosterRow(TrainingRoster row)
        {
            context.TrainingRosters.Remove(row);
        }

        protected override IQueryable<Training> GetEventSource()
        {
            return context.Trainings.Include("OfferedCourses").Include("Roster.TrainingAwards");
        }
    }
}
