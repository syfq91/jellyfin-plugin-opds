export default function (view){

    const Plugin = {
        pluginId: 'F30880AE-3365-449E-B9E6-BF133C8401B0',
        chkAllowAnonymous: document.querySelector('#chkAllowAnonymous'),
        chkEnablePageStreaming: document.querySelector('#chkEnablePageStreaming'),
        chkEnableProgression: document.querySelector('#chkEnableProgression'),
        txtDefaultPageSize: document.querySelector('#txtDefaultPageSize'),
        txtThumbnailWidth: document.querySelector('#txtThumbnailWidth'),
        txtAuthCacheDuration: document.querySelector('#txtAuthCacheDuration'),

        init: async function(){
            const config = await window.ApiClient.getPluginConfiguration(Plugin.pluginId);

            Plugin.chkAllowAnonymous.checked = config.AllowAnonymousAccess;
            Plugin.chkEnablePageStreaming.checked = config.EnablePageStreaming;
            Plugin.chkEnableProgression.checked = config.EnableProgression;
            Plugin.txtDefaultPageSize.value = config.DefaultPageSize || 50;
            Plugin.txtThumbnailWidth.value = config.ThumbnailWidth || 240;
            Plugin.txtAuthCacheDuration.value = config.AuthCacheDurationMinutes || 10;

            document.querySelector("#saveConfig").addEventListener("click", Plugin.saveConfig);
        },
        saveConfig: function(e){
            e.preventDefault();

            const config = {};

            config.AllowAnonymousAccess = Plugin.chkAllowAnonymous.checked;
            config.EnablePageStreaming = Plugin.chkEnablePageStreaming.checked;
            config.EnableProgression = Plugin.chkEnableProgression.checked;
            config.DefaultPageSize = parseInt(Plugin.txtDefaultPageSize.value, 10) || 50;
            config.ThumbnailWidth = parseInt(Plugin.txtThumbnailWidth.value, 10) || 240;
            config.AuthCacheDurationMinutes = parseInt(Plugin.txtAuthCacheDuration.value, 10) || 10;

            window.ApiClient.updatePluginConfiguration(Plugin.pluginId, config).then(Dashboard.processPluginConfigurationUpdateResult);
        }
    }

    view.addEventListener("viewshow", async function(){
        await Plugin.init();
    });
}
