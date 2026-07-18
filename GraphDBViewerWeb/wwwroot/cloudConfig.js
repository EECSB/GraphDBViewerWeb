//Cloud file-picker credentials. Fill in the keys for the providers you want to use,
//then whitelist this app's domain/origin in each provider's app settings. Leave a
//value empty to disable that provider's button in the file browser.
//
//Dropbox: create an app at https://www.dropbox.com/developers/apps (with the
//"Chooser" capability), add this site's domain under the app's Chooser/Saver
//domains, then paste the App key below.
//
//OneDrive: register an app at https://portal.azure.com (Azure AD app registrations),
//add this site's URL as a Single-page application redirect URI, then paste the
//Application (client) ID below.
//
//Google Drive: in https://console.cloud.google.com enable the "Google Picker API"
//and "Google Drive API", create an API key and an OAuth 2.0 Client ID (Web app),
//add this site to the OAuth client's authorized JavaScript origins, then paste both.
window.cloudPickerConfig = {
    dropboxAppKey: "",
    oneDriveClientId: "",
    googleApiKey: "",
    googleClientId: ""
};
