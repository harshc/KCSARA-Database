<%@ Page Title="" Language="C#" MasterPageFile="~/Views/Shared/Site.Master" Inherits="System.Web.Mvc.ViewPage<dynamic>" %>

<asp:Content ID="headContentSignup" ContentPlaceHolderID="HeadContent" runat="server">
    <link rel="stylesheet" type="text/css" href="/Content/signup.css" />
</asp:Content>
<asp:Content ID="Content1" ContentPlaceHolderID="MainContent" runat="server">

    <form id="form1" runat="server">

    <h2>Search and Rescue Volunteer Application</h2>
    <div style="max-width: 50em" id="application">
        
        <p>Welcome to King County Search and Rescue. Use the form below to start the application process with one or more of our <a target="_blank" href="http://kcsara.org/units/">member units</a>.</p>
        <p>Once you have created a basic profile you will continue the application process by adding contact information and other information before applying to a unit.</p>

        <fieldset>
            <legend>Tell us a little bit about yourself :</legend>
            <div>
                <div>
                    <label for="firstname" class="nameLabel">First Name: </label>
                    <input id="firstname" type="text" class="nameBox" data-bind="value: Firstname, watermark: Firstname.watertext" />
                </div>
                <div>
                    <label for="lastname" class="nameLabel">Last Name: </label>
                    <input id="lastname" type="text" class="nameBox" data-bind="value: Lastname, watermark: Lastname.watertext" />
                </div>                
                <div>
                    <label class="nameLabel" for="email">Email Address:</label>
                    <input class="nameBox" type="text" data-bind="value: Email, watermark: 'user@domain.com'" id="email" />
                </div>
                <div>
                    <label class="nameLabel" for="password">Password:</label>
                    <input type="password" class="nameBox" id="password" data-bind="value: Password, valueUpdate: 'afterkeydown'" />                    
                </div>
                <div>
                    <label for="password2" class="nameLabel">Reenter Password:</label>
                    <input type="password" class="nameBox" id="password2" data-bind="value: Password.Check, valueUpdate: 'afterkeydown'" />
                </div>
                <span style="color: red" data-bind="text: Password.Errors"></span><br />
            </div>
        </fieldset>        
        <button data-bind="jqButtonEnable: Ready() && !Working(), click: function() { doSubmit($root); }">Submit</button>        
    </div>
    <div>
        <legend>Already a member? <%=Html.ActionLink<AccountController>(f=>f.Login(), "Login") %></legend>
    </div>
    <script type="text/javascript">
        var PageModel = function () {
            var self = this;

            this.Firstname = ko.observable('');
            this.Firstname.watertext = "First Name";            
            this.Lastname = ko.observable('');
            this.Lastname.watertext = "Last Name";            
            this.Email = ko.observable('');
            this.Email.watertext = "user@domain.com";
            
            this.Password = ko.observable('');
            this.Password.Check = ko.observable('');
            this.Password.Errors = ko.computed(function () {
                if (this.Password() != this.Password.Check()) {
                    return "Passwords don't match";
                }
                return '';
            }, this);

            this.Ready = ko.observable();
            this.Ready.Tasks = [
                {
                    title: "Fill in the form", check: ko.computed(function () {
                        var ready = true;
                        var required = ["Firstname", "Lastname", "Email", "Password"];
                        for (var i = 0; i < required.length; i++) {
                            var f = required[i];
                            ready &= (this[f]() != "" && this[f]() != this[f].watertext);
                        }
                        return ready;
                    }, this)
                },
                {
                    title: "Select password (6 characters or more)", check: ko.computed(function () {
                        return this.Password().length >= 6 && this.Password.Errors() == '';
                    }, this)
                },
            ];

            this.Ready.Checker = ko.computed(function () {
                var ready = true;
                for (var i = 0; i < this.Ready.Tasks.length; i++) {
                    ready &= this.Ready.Tasks[i].check();
                }
                this.Ready(ready);
            }, this);

            this.Working = ko.observable(false);
        };

        function doSubmit(model) {
            model.Working(true);
            $.ajax({
                type: 'POST', url: '<%= Url.RouteUrl("defaultApi", new { httproute="", controller = "Account", action = "CreateAccount" }) %>',
                data: ko.toJSON(model), dataType: 'json', contentType: 'application/json; charset=utf-8'
            })
            .done(function (result) {
                if (result == true) {
                    window.location.href = '<%=  Url.Action("Verify") %>/' + model.Username();
                }               
            })
            .always(function () {
                model.Working(false);
            });
        }

        $(document).ready(function () {
            var a = $('#application');
            a.find('input[type="text"]').addClass('input-box');
            a.find('input[type="password"]').addClass('input-box');
            a.find('select').addClass('input-box');
            var model = new PageModel();
            ko.applyBindings(model);
            $('button').button();
        });
    </script>
    </form>
</asp:Content>

<asp:Content ID="Content3" ContentPlaceHolderID="secondaryNav" runat="server">
</asp:Content>
