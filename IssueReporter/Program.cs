using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NLog;

namespace IssueReporter
{
    class Program
    {
       
        public class ReportIssue
        {
            public ReportIssue(string t, string d, string f, string l, string st, string sc, string rc, string m)
            {
                Team = t;
                Developer = d;
                Function = f; 
                SeverityText = st;
                Message = m;
                // this ones should be converted becaue APL has upper minus for minus
                RuleCode = int.Parse(rc.Replace('¯', '-'));
                Line = 1+int.Parse(l.Replace('¯', '-'));
                if (Line == 0) Line++; // we can report only from line 1
                SeverityCode = int.Parse(sc.Replace('¯', '-'));

            }
            public string Team { get; set; }
            public string Developer { get; set; }
            public string Function { get; set; }
            public int RuleCode { get; set; }
            public string SeverityText { get; set; }
            public int SeverityCode { get; set; }
            public int Line { get; set; }
            public string Message { get; set; }                       

        }

        private static Logger _logger = LogManager.GetCurrentClassLogger();

        private static void Main(string[] args)
        {

            // Create SQConnector instance and load all data            
            SQConnector sqConnector = new SQConnector();
            sqConnector.LoadData();
            //// Delete all old rules DO NOT DO THIS!!!!!
            //_logger.Info("Delete old manual rules #" + sqConnector.Rules.Count);
            //foreach (var r in sqConnector.Rules)
            //{
            //    sqConnector.DeleteRule(r);
            //}           

            //TODO : parse report
            _logger.Info("Start parsing report...");
            List<ReportIssue> allReportedIssues = new List<ReportIssue>();
            System.IO.StreamReader report = new System.IO.StreamReader("c:\\SonarQube\\APL\\src\\report.txt");
            string line;
            while ((line = report.ReadLine()) != null)
            {
                string[] elms = line.Split('\t').Select(sValue => sValue.Trim()).ToArray();
                if (elms.Count() != 8)
                {
                    _logger.Error("Less then 8 elements in report line  - "+line);
                }
                // hard coded format                
                else
                {
                    allReportedIssues.Add(new ReportIssue(elms[0], elms[1], elms[2], elms[3], elms[4], elms[5], elms[6], elms[7]));
                }                
            }
            report.Close();
            _logger.Info("Report parsed. {0} issies fetched",allReportedIssues.Count);
            // Remove all OK reports 
            allReportedIssues.RemoveAll(i => i.RuleCode == -1);
            allReportedIssues.RemoveAll(i => i.SeverityCode == 10);
            _logger.Info("OK and Info issues deleted. Issues left - {0}",allReportedIssues.Count);


            //TODO : update rules and Issues based on report
            SQConnector.Rule rule;
            SQConnector.Component component;
            SQConnector.Issue issue;
            List<ReportIssue> errorReportIssues = new List<ReportIssue>();
            Regex pattern = new Regex(@"[^\d]");            
            int missing_components_num = 0;
            int added_rules_num = 0;
            int created_issues_num = 0;
            int already_reported_issues_num = 0;
            int issues_on_missing_component = 0;            
            foreach (ReportIssue rissue in allReportedIssues)
            {
                // if issue is already reported - skip
                if (!errorReportIssues.Contains(rissue))
                {
                    try
                    {
                        if (null ==
                            sqConnector.Issues.Find(
                                i =>
                                    i.component.Contains(rissue.Function) && i.rule.Contains(rissue.RuleCode.ToString()) &&
                                    i.line == rissue.Line))
                        {
                            //if not - check if component present
                            component = sqConnector.Files.Find(c => c.key.Contains(rissue.Function));
                            if (component == null)
                            {
                                _logger.Warn(
                                    "Component is missimg! Can not add issue! Function name from report - {0}",
                                    rissue.Function);
                                missing_components_num++;
                                // remove all reported issues corresponding to this component
                                errorReportIssues.AddRange(allReportedIssues.FindAll(r => r.Function == rissue.Function));
                            }
                            else
                            {
                                //check if rule present                                    
                                try
                                {
                                    rule =
                                        sqConnector.Rules.Find(
                                            r => Int32.Parse(pattern.Replace(r.key, "")) == rissue.RuleCode);
                                    // rule not found, create new rule
                                    if (rule == null)
                                    {
                                        rule = sqConnector.AddRule(rissue.RuleCode, rissue.Message);
                                        _logger.Info("New rule created: {0}", rule.key);
                                        added_rules_num++;
                                    }
                                    //create new issue
                                    issue = sqConnector.AddIssue(rule, component, rissue.Line, rissue.Message,
                                        rissue.SeverityCode);
                                    created_issues_num++;
                                }
                                catch (FormatException ex)
                                {
                                    _logger.Error("Bad manual rules are present!");
                                    sqConnector.Rules.RemoveAll(r => pattern.Replace(r.key, "").Length == 0);
                                }
                            }
                        }
                        else
                            already_reported_issues_num++;
                    }
                    catch (ArgumentNullException)
                    {
                        _logger.Error("Error on issue searching");
                    }

                }
                else
                {
                    issues_on_missing_component++;
                }
            }
            //Mark as Fixed issues not present any more in report            
           var updateList =
                sqConnector.Issues.FindAll(
                    _issue =>
                        _issue.status == "OPEN" &&
                        !allReportedIssues.Exists(
                            r =>
                                r.Line == _issue.line && _issue.rule.Contains(r.RuleCode.ToString()) &&
                                _issue.component.Contains(r.Function)));
            List<SQConnector.Issue> notUpdated = new List<SQConnector.Issue>();
            int _bulkSize = 50;
            int numToUpdate = updateList.Count;
            int _loopCount = numToUpdate/_bulkSize;
            if (numToUpdate%_bulkSize != 0)
                _loopCount++;
            int num;
            for (int i = 0; i < _loopCount; i++)
            {
                num = _bulkSize;
                if ((i + 1)*_bulkSize > numToUpdate)
                    num = numToUpdate - i*_bulkSize;
                notUpdated.AddRange(sqConnector.FixIssues(updateList.GetRange(i*_bulkSize, num))); 
            }
            

            _logger.Info("Issues reporting finised.");
            _logger.Info("Missing components - {0}",missing_components_num);
            _logger.Info("Issues on missing components - {0}",issues_on_missing_component);
            _logger.Info("ALready reported issues - {0}",already_reported_issues_num);
            _logger.Info("Rules added - {0}",added_rules_num);
            _logger.Info("Issues added - {0}",created_issues_num);
            _logger.Info("Issues fixed - {0}",(updateList.Count - notUpdated.Count));
            _logger.Error("Following issues where not updated:");
            foreach (var _issue in notUpdated)
            {
                _logger.Error("Key:{0}\tComponent:{1}\tStatus:{2}",_issue.key,_issue.component,_issue.status);
                
            }

        }

    }



}