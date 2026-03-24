# IcM MCP Tools

Generated: 2026-03-24 04:16:16 -07:00

Total tools: 23

| # | Tool | Description | Parameters | Example arguments (JSON) |
|---:|---|---|---|---|
| 1 | get_impacted_s500_customers | Get impacted S500 customers for given incident or outage. | incidentId:integer (required) | {"incidentId":139979555} |
| 2 | get_impacted_ace_customers | Get impacted ACE customers for given incident or outage. | incidentId:integer (required) | {"incidentId":139979555} |
| 3 | get_impacted_services_regions_clouds | Get affected services, regions and clouds for given incident or outage. | incidentId:integer (required) | {"incidentId":139979555} |
| 4 | get_teams_by_public_id | Get team details by team public Id. Public Id looks like TenantName\TeamName | publicId:string (required) | {"publicId":139979555} |
| 5 | search_incidents_by_owning_team_id | This tool Searches for incidents by owning team's id. | teamId:integer (required) | {"teamId":139979555} |
| 6 | get_impacted_azure_priority0_customers | Get impacted 'Azure Priority 0' or 'Life and Safety' customers for given incident or outage. | incidentId:integer (required) | {"incidentId":139979555} |
| 7 | get_impacted_subscription_count | Get impacted subscription count for given incident or outage. | incidentId:integer (required) | {"incidentId":139979555} |
| 8 | get_support_requests_crisit | Get support requests/support tickets (SRs) and SevA (CritSit) linked to given incident/outage. | incidentId:integer (required) | {"incidentId":139979555} |
| 9 | get_incident_location | Get location information of the incident and/or outage, including region, availability zone, data center, cluster, node, and region arm alias. | incidentId:string (required) | {"incidentId":139979555} |
| 10 | get_teams_by_name | Get team details by team name. | teamName:string (required) | {"teamName":"sample"} |
| 11 | get_on_call_schedule_by_team_id | Get the on-call schedule for a team by team Id. | teamIds:array (required) | {"teamIds":[]} |
| 12 | get_incident_customer_impact | Provide overall impact for the specified incident or outage. | incidentId:integer (required) | {"incidentId":139979555} |
| 13 | get_outage_high_priority_events | Get impacted High Priority events for given incident or outage. | incidentId:integer (required) | {"incidentId":139979555} |
| 14 | get_ai_summary | Get incident and/or outage summary and only for summary. | incidentId:string (required) | {"incidentId":139979555} |
| 15 | get_team_by_id | Get team details by team Id. | teamId:integer (required) | {"teamId":139979555} |
| 16 | get_contact_by_alias | Get contact details by contact alias. | alias:string (required) | {"alias":"sample"} |
| 17 | get_contact_by_id | Get contact details by contact Id. | contactId:integer (required) | {"contactId":139979555} |
| 18 | get_mitigation_hints | Get mitigation hints for a given incident id. | incidentId:integer (required) | {"incidentId":139979555} |
| 19 | get_incident_context | Provide all detailed context information, all original metadata for the incident and outage | incidentId:string (required) | {"incidentId":139979555} |
| 20 | is_specific_customer_impacted | Check whether a specific customer is in the impacted customer list by the incident/outage id. Note: Even if the result is false, doesn't mean the customer is not impacted. Ask user to check other impact metric like Support requests, Sev A (CritSit) etc. | incidentId:integer (required), customerName:string (required) | {"incidentId":139979555,"customerName":"sample"} |
| 21 | get_services_by_names | Get the services details by list of names. | names:array (required) | {"names":[]} |
| 22 | get_incident_details_by_id | Get details of an incident for a given incident id. | incidentId:integer (required) | {"incidentId":139979555} |
| 23 | get_similar_incidents | Get a list of similar incidents for a given incident id. | incidentId:integer (required) | {"incidentId":139979555} |
