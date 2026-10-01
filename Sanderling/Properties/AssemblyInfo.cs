using System.Runtime.InteropServices;
using Rhino.PlugIns;

// Plug-in description (shown in Rhino's PlugInManager)
[assembly: PlugInDescription(DescriptionType.Address, "")]
[assembly: PlugInDescription(DescriptionType.Country, "")]
[assembly: PlugInDescription(DescriptionType.Email, "")]
[assembly: PlugInDescription(DescriptionType.Phone, "")]
[assembly: PlugInDescription(DescriptionType.Fax, "")]
[assembly: PlugInDescription(DescriptionType.Organization, "")]
[assembly: PlugInDescription(DescriptionType.UpdateUrl, "")]
[assembly: PlugInDescription(DescriptionType.WebSite, "")]

[assembly: ComVisible(false)]

// Unique plug-in ID - do not change after release
[assembly: Guid("db321efe-67e5-460e-9076-c687ad841558")]
