using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using NLog;

namespace IssueReporter
{
    public class SQConnector
    {
        private static Logger _logger = LogManager.GetCurrentClassLogger();

        public List<Rule> Rules = new List<Rule>();
        public List<Issue> Issues = new List<Issue>();
        public List<Component> Files = new List<Component>();
        public bool dataLoaded = false;

        private static string SQServerUrl = "http://ua01ws0097:9000/";
        private static string SQAdminLogin = "admin";
        private static string SQAdminPassword = "admin";
        private static string GET_RULES = "api/rules/search?ps=500&repositories=manual&p=";
        private static string GET_ISSUES = "api/issues/search?ps=500&p=";
        private static string GET_FILES = "api/components/tree?ps=500&baseComponentKey=SCD:Accounting&qualifiers=FIL&p=";
        private static string POST_RULE = "api/rules/create";
        private static string POST_ISSUE = "api/issues/create";
        //private static string POST_DELETE_RULE = "api/rules/delete";
        private static string POST_BULK_CHANGE_ISSUE = "api/issues/bulk_change";

        public void LoadData()
        {
            _logger.Info("Loading data ...");
            //load all rules
            int rpage = 1;
            Rules = new List<Rule>();
            var response = SendGet(GET_RULES, rpage);
            RulesCollection rulesCollection = JsonConvert.DeserializeObject<RulesCollection>(response);
            Rules.AddRange(rulesCollection.rules);
            while (Rules.Count < rulesCollection.total)
            {
                rpage++;
                response = SendGet(GET_RULES, rpage);
                rulesCollection = JsonConvert.DeserializeObject<RulesCollection>(response);
                Rules.AddRange(rulesCollection.rules);
            }
            //Load all  issues
            int ipage = 1;
            Issues = new List<Issue>();
            response = SendGet(GET_ISSUES, ipage);
            IssueCollection issuesCollection = JsonConvert.DeserializeObject<IssueCollection>(response);
            Issues.AddRange(issuesCollection.issues);
            while (Issues.Count < issuesCollection.total)
            {
                ipage++;
                response = SendGet(GET_ISSUES, ipage);
                issuesCollection = JsonConvert.DeserializeObject<IssueCollection>(response);
                Issues.AddRange(issuesCollection.issues);
            }
            //load all components
            int fpage = 1;
            response = SendGet(GET_FILES, fpage);
            ComponentCollectiont fileCollection = JsonConvert.DeserializeObject<ComponentCollectiont>(response);
            Files.AddRange(fileCollection.components);
            while (Files.Count < fileCollection.paging.total)
            {

                fpage++;
                response = SendGet(GET_FILES, fpage);
                fileCollection = JsonConvert.DeserializeObject<ComponentCollectiont>(response);
                Files.AddRange(fileCollection.components);
            }

            _logger.Info("Data loaded. Pages: Issues-{0} Files-{1} Rules-{2}", ipage, fpage, rpage);
            dataLoaded = true;
        }

        private string SendGet(string get_query, int page)
        {
            using (WebClient client = new WebClient())
            {
                string q = SQServerUrl + get_query + page.ToString();
                try
                {
                    return client.DownloadString(SQServerUrl + get_query + page.ToString());
                }
                catch (WebException)
                {
                    _logger.Error("WebException on GET = " + q);
                    //Console.ReadKey();
                    return "";
                }

            }

        }

        private string SendPost(string post_query, NameValueCollection post_parameters)
        {
            using (WebClient client = new WebClient())
            {
                client.Credentials = new NetworkCredential(SQAdminLogin, SQAdminPassword);
                byte[] responsebytes = null;
                try
                {
                    responsebytes = client.UploadValues(SQServerUrl + post_query, "POST", post_parameters);
                }
                catch (WebException)
                {
                    //if 401 this is OK
                    //Console.WriteLine(ex.Message);
                    string credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes(SQAdminLogin + ":" + SQAdminPassword));
                    client.Headers[HttpRequestHeader.Authorization] = string.Format("Basic {0}", credentials);
                    responsebytes = client.UploadValues(SQServerUrl + post_query, "POST", post_parameters);
                }
                return Encoding.UTF8.GetString(responsebytes);
            }
        }

        /*public bool DeleteRule(Rule rule)
        {
            NameValueCollection data = new NameValueCollection();
            data.Add("key",rule.key);
            var response = SendPost(POST_DELETE_RULE, data);
            return response != null;            
        }*/

        public Rule AddRule(int rule_code, string desc)
        {
            //Post to SQ and add to collection
            NameValueCollection data = new NameValueCollection();
            data.Add("manual_key", rule_code.ToString());
            data.Add("name", "AFC-" + rule_code.ToString());
            data.Add("markdown_description", desc);
            var response = SendPost(POST_RULE, data);
            Rule newRule = JsonConvert.DeserializeObject<PostRuleResponse>(response).rule;
            Rules.Add(newRule);
            return newRule;
        }

        /*public void RevokeRule(Rule rule)
        {            
            //Post to SQ and add to collection
            NameValueCollection data = new NameValueCollection();
            data.Add("key", rule.key);
            data.Add("status", "READY");            
            var response = SendPost(POST_RULE, data);
        }*/

        public Issue AddIssue(Rule rule, Component component, int line, string message, int severity)
        {
            // POST to SQ and add to collection
            NameValueCollection data = new NameValueCollection();
            data.Add("component", component.key);
            data.Add("line", line.ToString());
            data.Add("message", message);
            data.Add("rule", rule.key);
            data.Add("severity", ((SeverityCode)severity).ToString());
            var response = SendPost(POST_ISSUE, data);
            Issue newIssue = JsonConvert.DeserializeObject<PostIssueResponse>(response).issue;
            Issues.Add(newIssue);
            return newIssue;
        }

        public string[] UpdateIssuesInBulk(List<Issue> issues, string[] parameters, string[] new_values)
        {
            //POST Update of Issues in bulk
            NameValueCollection data = new NameValueCollection();
            if (parameters.Length != new_values.Length)
            {
                var err = string.Format("Number of parameters({0}) not equal to number of values({1}).", parameters.Length, new_values.Length);
                throw new Exception(err);
            }
            for (int i = 0; i < parameters.Length; i++)
            {
                data.Add(parameters[i], new_values[i]);
            }
            var response = SendPost(POST_BULK_CHANGE_ISSUE, data);
            return  JsonConvert.DeserializeObject<PostUpdateInBulkResponse>(response).issuesNotChanged.issues;
                       
        }

        public List<Issue> FixIssues(List<Issue> updateList)
        {
            string[] parameters = new[] { "actions", "do_transition.transition", "issues" };
            string[] new_values = new[] { "do_transition", "resolve", ""};
            new_values[2] = string.Join(",", updateList.Select(issue => issue.key).ToArray());
            var notUpdatedKeys = this.UpdateIssuesInBulk(updateList,parameters,new_values);
            return updateList.FindAll(i => notUpdatedKeys.Contains(i.key));
        }
        public class PostUpdateInBulkResponse
        {
            public Issueschanged issuesChanged { get; set; }
            public Issuesnotchanged issuesNotChanged { get; set; }
        }

        public class Issueschanged
        {
            public int total { get; set; }
        }

        public class Issuesnotchanged
        {
            public int total { get; set; }
            public string[] issues { get; set; }
        }

        public class PostRuleResponse
        {
            public Rule rule { get; set; }
        }

        public enum SeverityCode
        {
            INFO = 10, // Info in report
            MINOR = 3, // Warning in report
            MAJOR = 2  // Error in report                             
        }

        public class PostIssueResponse
        {
            public Issue issue { get; set; }
            public Component[] components { get; set; }
            public Rule[] rules { get; set; }
            public User[] users { get; set; }
            public object[] actionPlans { get; set; }
        }

        public class User
        {
            public string login { get; set; }
            public string name { get; set; }
            public string email { get; set; }
            public bool active { get; set; }
        }

        public class ComponentCollectiont
        {
            public Paging paging { get; set; }
            public Basecomponent baseComponent { get; set; }
            public Component[] components { get; set; }
        }

        public class Paging
        {
            public int pageIndex { get; set; }
            public int pageSize { get; set; }
            public int total { get; set; }
        }

        public class Basecomponent
        {
            public string id { get; set; }
            public string key { get; set; }
            public string name { get; set; }
            public string qualifier { get; set; }
        }

        public class Component
        {
            public string id { get; set; }
            public string key { get; set; }
            public string name { get; set; }
            public string qualifier { get; set; }
            public string path { get; set; }
        }

        public class IssueCollection
        {
            public int total { get; set; }
            public int p { get; set; }
            public int ps { get; set; }
            public IssuePaging paging { get; set; }
            public Issue[] issues { get; set; }
            public IssueComponent[] components { get; set; }
        }

        public class IssuePaging
        {
            public int pageIndex { get; set; }
            public int pageSize { get; set; }
            public int total { get; set; }
        }

        public class Issue
        {
            public string key { get; set; }
            public string rule { get; set; }
            public string severity { get; set; }
            public string component { get; set; }
            public int componentId { get; set; }
            public string project { get; set; }
            public int line { get; set; }
            public Flow[] flows { get; set; }
            public string resolution { get; set; }
            public string status { get; set; }
            public string message { get; set; }
            public string reporter { get; set; }
            public string author { get; set; }
            public string[] tags { get; set; }
            public DateTime creationDate { get; set; }
            public DateTime updateDate { get; set; }
            public Textrange textRange { get; set; }
            public string debt { get; set; }
            public DateTime closeDate { get; set; }

            //public Issue()
            //{

            //}

            //public Issue(Rule rule, Component component, int line, string message, int severity)
            //{
            //    //TOD: add asignee
            //    using (WebClient client = new WebClient())
            //    {
            //        string POST_ISSUE = "http://localhost:9000/api/issues/create?";
            //        string POST_ISSUE_PARAMETERS = "component=" + component.key + "&line=" + line + " &message = " + message + "&rule=" + rule.key + "&severity=" + ((SeverityCode)severity).ToString();
            //        var response = client.UploadString(POST_ISSUE, POST_ISSUE_PARAMETERS);
            //        Issue r = JsonConvert.DeserializeObject<Issue>(response);

            //        this.key = r.key;
            //        this.rule = r.rule;
            //        this.severity = r.severity;
            //        this.component = r.component;
            //        this.componentId = r.componentId;
            //        this.project = r.project;
            //        this.line = r.line;
            //        this.flows = r.flows;
            //        this.resolution = r.resolution;
            //        this.status = r.status;
            //        this.message = r.message;
            //        this.reporter = r.reporter;
            //        this.author = r.author;
            //        this.tags = r.tags;
            //        this.creationDate = r.creationDate;
            //        this.updateDate = r.updateDate;
            //        this.textRange = r.textRange;
            //        this.debt = r.debt;
            //        this.closeDate = r.closeDate;
            //    }
            //}
        }

        public class Textrange
        {
            public int startLine { get; set; }
            public int endLine { get; set; }
            public int startOffset { get; set; }
            public int endOffset { get; set; }
        }

        public class Flow
        {
            public Location[] locations { get; set; }
        }

        public class Location
        {
            public Textrange1 textRange { get; set; }
            public string msg { get; set; }
        }

        public class Textrange1
        {
            public int startLine { get; set; }
            public int endLine { get; set; }
            public int startOffset { get; set; }
            public int endOffset { get; set; }
        }

        public class IssueComponent
        {
            public int id { get; set; }
            public string key { get; set; }
            public string uuid { get; set; }
            public bool enabled { get; set; }
            public string qualifier { get; set; }
            public string name { get; set; }
            public string longName { get; set; }
            public string path { get; set; }
            public int projectId { get; set; }
            public int subProjectId { get; set; }
        }

        public class RulesCollection
        {
            public int total { get; set; }
            public int p { get; set; }
            public int ps { get; set; }
            public Rule[] rules { get; set; }
        }

        public class Rule
        {
            public string key { get; set; }
            public string repo { get; set; }
            public string name { get; set; }
            public DateTime createdAt { get; set; }
            public string htmlDesc { get; set; }
            public string mdDesc { get; set; }
            public string status { get; set; }
            public bool isTemplate { get; set; }
            public object[] tags { get; set; }
            public object[] sysTags { get; set; }
            public object[] _params { get; set; }
            public bool debtOverloaded { get; set; }

            //public Rule(int rulecode, string message)
            //{
            //    using (WebClient client = new WebClient())
            //    {
            //        string POST_RULE = "http://localhost:9000/api/rules/create?" + "manual_key=" + rulecode.ToString() + "&name=AFC-" + rulecode.ToString() + "&markdown_description = " + message;
            //        string POST_RULE_PARAMETERS = "manual_key=" + rulecode + "name=AFC-" + rulecode + "markdown_description = " + message;
            //        var response = client.UploadString(POST_RULE, "");
            //        Rule r = JsonConvert.DeserializeObject<Rule>(response);

            //        // following should be done easier somehow ...
            //        key = r.key;
            //        repo = r.repo;
            //        createdAt = r.createdAt;
            //        htmlDesc = r.htmlDesc;
            //        mdDesc = r.mdDesc;
            //        status = r.status;
            //        isTemplate = r.isTemplate;
            //        tags = r.tags;
            //        sysTags = r.sysTags;
            //        _params = r._params;
            //        debtOverloaded = r.debtOverloaded;
            //    }

            //}

        }
       
    }

}