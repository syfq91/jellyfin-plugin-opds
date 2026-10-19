export default function (view){

    const Plugin = {
        pluginId: 'F30880AE-3365-449E-B9E6-BF133C8401B0',
        chkAllowAnonymous: document.querySelector('#chkAllowAnonymous'),
        chkEnablePageStreaming: document.querySelector('#chkEnablePageStreaming'),
        chkEnableProgression: document.querySelector('#chkEnableProgression'),

        init: async function(){
            const config = await window.ApiClient.getPluginConfiguration(Plugin.pluginId);

            Plugin.chkAllowAnonymous.checked = config.AllowAnonymousAccess;
            Plugin.chkEnablePageStreaming.checked = config.EnablePageStreaming;
            Plugin.chkEnableProgression.checked = config.EnableProgression;

            document.querySelector("#saveConfig").addEventListener("click", Plugin.saveConfig);
        },
        saveConfig: function(e){
            e.preventDefault();

            const config = {};

            config.AllowAnonymousAccess = Plugin.chkAllowAnonymous.checked;
            config.EnablePageStreaming = Plugin.chkEnablePageStreaming.checked;
            config.EnableProgression = Plugin.chkEnableProgression.checked;

            window.ApiClient.updatePluginConfiguration(Plugin.pluginId, config).then(Dashboard.processPluginConfigurationUpdateResult);
        }
    }

    view.addEventListener("viewshow", async function(){
        await Plugin.init();
    });
}
