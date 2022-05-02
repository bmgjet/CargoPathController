# CargoPathController
<p>Make Better CargoPaths For Custom Maps. <br /> To use this plugin you must be admin on the server. <br /><br /> <span style="text-decoration: underline;"><strong>Console Command</strong></span> <br /> createcargopath "newname.map" minwaterdepth mindistancefromprefabs smoothing<br />(Creates and saves new map file with cargopath of these settings)<br /><br /> createcargopath "newname.map" <br />(Saves the cargopath into a new mapfile, Will be native path if none had been created already)<br /><br /> stopcargopath <br />(Stops the cargo path generator if its taking too long) <br /> <br /> <span style="text-decoration: underline;"><strong>Chat Commands</strong></span><br /> /createcargopath "newname.map" minwaterdepth mindistancefromprefabs smoothing <br />(Creates and saves new map file with cargopath of these settings)<br /><br /> /showcargopath <br />(Shows the current loaded cargopath (squares nodes wont let cargo leave or spawn on))<br /><br /> /addcargopath nodeindex <br />(Adds at node at that index, Using here instead of number will add between the closest nodes)<br /><br /> /removecargopath nodeindex nodeindex <br />(Removes node at that index or all between 2 given indexes)<br /><br /> /savecargopath "newmapname.map" <br />(Saves current cargopath into new mapfile)<br /><br /> /blockcargopath blocksize <br />(Changes Topology below player to what you have set to block cargo egrees)<br /><br /><span style="text-decoration: underline;"><strong>Settings</strong></span></p>
<p>public int TopologyBaseBlock = TerrainTopology.OCEANSIDE;<br />(Change OCEANSIDE to another topology if you dont want to use that for blocking cargo ship)<br />Other wise when the cargoship is in that topology it will not be able to leave until its free of that topology.<br />It will also not spawn on any node thats in that topology.)<br /><br /><span style="text-decoration: underline;"><strong>Video</strong></span></p>
<p>https://www.youtube.com/watch?v=BJVPhlCKWWE</p>
<p>&nbsp;</p>