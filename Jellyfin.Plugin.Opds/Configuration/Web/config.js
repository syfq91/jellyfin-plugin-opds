export default function (view){

    const Plugin = {
        pluginId: 'F30880AE-3365-449E-B9E6-BF133C8401B0',
        chkAllowAnonymous: document.querySelector('#chkAllowAnonymous'),
        chkEnableSyncServer: document.querySelector('#chkEnableSyncServer'),
        chkEnableUserRegistration: document.querySelector('#chkEnableUserRegistration'),

        init: async function(){
            const config = await window.ApiClient.getPluginConfiguration(Plugin.pluginId);

            Plugin.chkAllowAnonymous.checked = config.AllowAnonymousAccess;
            Plugin.chkEnableSyncServer.checked = config.EnableSyncServer;
            Plugin.chkEnableUserRegistration.checked = config.EnableUserRegistration;

            document.querySelector("#saveConfig").addEventListener("click", Plugin.saveConfig);
        },
        saveConfig: function(e){
            e.preventDefault();

            const config = {};

            config.AllowAnonymousAccess = Plugin.chkAllowAnonymous.checked;
            config.EnableSyncServer = Plugin.chkEnableSyncServer.checked;
            config.EnableUserRegistration = Plugin.chkEnableUserRegistration.checked;

            window.ApiClient.updatePluginConfiguration(Plugin.pluginId, config).then(Dashboard.processPluginConfigurationUpdateResult);
        }
    }

    view.addEventListener("viewshow", async function(){
        await Plugin.init();
    });
}
