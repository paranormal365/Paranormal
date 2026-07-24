# Organization Security

## Organization Owner and Administrators

The owner and administrators of an organization, by default, have all CRUD security enabled.  They are also the only ones who can create new roles for their organization.  These roles have no impact on the Users' website roles.  They only have impacts on the Users' permissions in relation to the given organization.  When a new organization is created, two uneditable roles are created for the group.  The owner - who is the person who created the group but can be reassigned if the owner chooses to give the ownership to someone else. The other uneditable role with all CRUD permissions is Administrator.  

## Notes about Organizations

Anyone can create their own organization and they are immediately assigned as the owner of the organization.  Organizations have unique UrlNames.  These names allow them to be able to generate links to their organization pages within the application.  So, if the application is https://www.ishaunted.com and their organization UrlName is "spooky-ben", they can provide the link https://www.ishaunted.com/spooky-ben to direct the public to their applicaiton home page.  There will be future updates to this where they will be able provide links within their organization page to direct users to specific pages.

Another note to remember is that users can own and be members of more than one organization.  

## Roles

The owner and administrators are able to create named roles for their organization.  When creating the role, they will provide a name for the role and they will go through the hierarchy of functionality provided for organizations and specify if members of the role have Create, Update, Read and/or Delete permission.  

Members of the organization can have more than one role.  While the default is false permission for each of the CRUD security in each of the sections of functionality, when a member has multiple roles, if one of the roles is active and the permission is true, it overrides the false default.

So, if a user has a hypothetical role of "Designer" and that role has all CRUD permission for the CMS for the organization and they have another hypothetical role of "User Acceptance" which has the ability for Review and Update is true allowing them to see users who have applied to be members and the ability to accept or deny them, but the "User Acceptance" role has no permission for CMS, that user would still have all CRUD permission for CMS and the ability to Review and Update users who applied for membership.

The same situation as mentioned in the previous paragraph would apply to only the Organization where they have those permissions.  If the same user is a member of another group and has different roles or permissions than the Organziation above, they would only have the permissions of the roles they are assigned in the new group.

## Sections

In my explanation of organization security, I refer to sections almost like I refer to tables.  Sometimes, we need special permissions on top of sections and I am not sure what to call them.  For instance, we might allow members CRUD to add addresses and phone number records to the organization, but maybe for them to be included or public for the group and displayed maybe we have security around it being public or published or the order in which they appear assigned with their own security permissions.  When displaying the permissions for the role creation, it would appear for CRUD like toggle buttons or checkboxes would be like address permission, but the publishing, making it public or the order it appears underneath the row of address permissions but indented to indicate it is hierarchically related.

## Addresses and Mapping

For Addresses of organizations, when they are created, a process runs in the backgorund to get the latitude and longitude for the address.  This is the precise location of the address.  This will need the ability to include it on a map for the organization.  Also, it may be the case the organization doesn't want the exact address mapped and instead a region mapped around the address.  So, they would need to be able to 1) Choose it is mapped or not, 2) Color of map point, 3) possible Icon appearing on map, 4) If not displaying exact address they need to specify the miles around the point to map a region, 5) choose if they want both the map point or icon and region shaded.  The component where the organization manages mapping should include these options and display a map which updates when the organization user makes changes.  If they choose to show a region and don't exactly know how big in miles to make it, show them a region on the map and let them update it by dragging the circle region to the size they want and update the miles in the component form.

