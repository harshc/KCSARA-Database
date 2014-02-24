<%@ Page Title="" Language="C#" MasterPageFile="~/Views/Shared/Site.Master" Inherits="System.Web.Mvc.ViewPage<dynamic>" %>
<asp:Content ID="headContentTrainingHome" ContentPlaceHolderID="HeadContent" runat="server">
    <link rel="stylesheet" type="text/css" href="~/Content/common.less" />
</asp:Content>

<asp:Content ID="Content1" ContentPlaceHolderID="MainContent" runat="server">

<h2>Explorer Search and Rescue Training</h2>
    <div class="bodytext">
        <p>King County Explorer Search &amp; Rescue (ESAR) has an intensive 120 hour basic training course, held on alternating weekends in the fall and winter. The course includes two indoor orientation and information sessions, and four outdoor sessions held at one of the local Boy Scout camps.</p>
        <p>Lorem ipsum dolor sit amet, consectetur adipiscing elit. Vivamus lorem orci, porta non luctus eget, luctus id dui. Nullam sollicitudin nisl a leo lobortis blandit. Fusce ultrices mi sed odio lobortis, non eleifend velit gravida. Quisque mattis facilisis laoreet. Sed bibendum cursus odio, at pharetra elit eleifend id. Donec quis est consequat nunc pharetra interdum vel non metus. Suspendisse arcu libero, pellentesque ultrices interdum sit amet, pharetra ornare mi. Ut iaculis interdum lorem, et commodo ipsum. Fusce congue volutpat erat, sit amet hendrerit sem rhoncus id. Suspendisse eget egestas mi. Mauris a lorem quis nisl sodales lobortis. Mauris in ipsum diam. Aenean placerat, est vel auctor elementum, dui lectus consequat dolor, et dictum arcu purus eu nunc. </p>
        <p>Suspendisse eu turpis sit amet ipsum ornare pharetra ut non justo. Maecenas et facilisis tellus, nec tincidunt erat. Aenean gravida felis sed dui elementum, a scelerisque eros ullamcorper. Fusce vestibulum cursus libero vitae gravida. Vivamus in rutrum nisi. Aliquam erat volutpat. Etiam semper dolor non sapien fringilla, ut porta mauris ultricies. </p>
        <h3>Online Training</h3>
        <p>I am ready to start looking at the information myself. Our first course- Course A is available right here and will help you understand more about the organization. This will also introduce you to the overall training program and help you be prepared for the outdoor courses.
        <b><%=Html.ActionLink<AccountController>(f=>f.Signup(), "Login and Launch Course A")%></b></p>
        <h3>In Person</h3>
        <p>I am not sure yet. I would like to attend an in person event to learn more.
        <b><a href="">Course A Schedule</a></b></p>
    </div>
</asp:Content>