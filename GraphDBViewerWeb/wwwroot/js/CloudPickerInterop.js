//Cloud file-picker interop. Each provider's official picker SDK is loaded on demand
//and returns an "anyone with the link" URL for the selected file (or null on cancel).
//Credentials come from cloudConfig.js; a provider with no key configured is skipped.
window.cloudPicker = {

    getConfig: function () {
        return window.cloudPickerConfig || {};
    },

    //Reports which providers have credentials configured (used to enable/disable buttons).
    status: function () {
        const c = this.getConfig();
        return {
            dropbox: !!c.dropboxAppKey,
            oneDrive: !!c.oneDriveClientId,
            google: !!(c.googleApiKey && c.googleClientId)
        };
    },

    //File extensions to offer for a given field ('image' or 'model').
    extensionsFor: function (filter) {
        if (filter === 'model')
            return ['.obj', '.zip'];
        else
            return ['.png', '.jpg', '.jpeg', '.svg'];
    },

    //Injects an external <script> once, resolving when it has loaded.
    loadScript: function (src, attrs) {
        return new Promise((resolve, reject) => {
            const existing = document.querySelector('script[data-cloudpicker="' + src + '"]');
            if (existing) {
                resolve();
                return;
            }

            const s = document.createElement('script');
            s.src = src;
            s.async = true;
            s.setAttribute('data-cloudpicker', src);

            if (attrs) {
                Object.keys(attrs).forEach(k => s.setAttribute(k, attrs[k]));
            }

            s.onload = () => resolve();
            s.onerror = () => reject(new Error('Failed to load ' + src));
            document.head.appendChild(s);
        });
    },

    //Entry point from Blazor. provider: 'dropbox'|'onedrive'|'google'; filter: 'image'|'model'.
    pick: async function (provider, filter) {
        try
        {
            if (provider === 'dropbox')
                return await this.pickDropbox(filter);
            else if (provider === 'onedrive')
                return await this.pickOneDrive(filter);
            else if (provider === 'google')
                return await this.pickGoogle(filter);
            else
                return null;
        }
        catch (e)
        {
            console.warn('Cloud picker error:', e);
            return null;
        }
    },

    pickDropbox: async function (filter) {
        const c = this.getConfig();
        if (!c.dropboxAppKey) {
            console.warn('Dropbox app key not set in cloudConfig.js');
            return null;
        }

        await this.loadScript('https://www.dropbox.com/static/api/2/dropins.js', { id: 'dropboxjs', 'data-app-key': c.dropboxAppKey });

        const self = this;
        return new Promise(resolve => {
            window.Dropbox.choose({
                linkType: 'direct',
                multiselect: false,
                extensions: self.extensionsFor(filter),
                success: files => {
                    if (files && files.length)
                        resolve(files[0].link);
                    else
                        resolve(null);
                },
                cancel: () => resolve(null)
            });
        });
    },

    pickOneDrive: async function (filter) {
        const c = this.getConfig();
        if (!c.oneDriveClientId) {
            console.warn('OneDrive client ID not set in cloudConfig.js');
            return null;
        }

        await this.loadScript('https://js.live.net/v7.2/OneDrive.js');

        const clientId = c.oneDriveClientId;
        return new Promise(resolve => {
            window.OneDrive.open({
                clientId: clientId,
                action: 'share',
                multiSelect: false,
                advanced: { redirectUri: window.location.origin },
                success: response => {
                    let url = null;

                    if (response && response.value && response.value.length) {
                        const item = response.value[0];
                        if (item.permissions && item.permissions.length && item.permissions[0].link)
                            url = item.permissions[0].link.webUrl;
                        else if (item.webUrl)
                            url = item.webUrl;
                    }

                    resolve(url);
                },
                cancel: () => resolve(null),
                error: e => {
                    console.warn('OneDrive error:', e);
                    resolve(null);
                }
            });
        });
    },

    pickGoogle: async function (filter) {
        const c = this.getConfig();
        if (!c.googleApiKey || !c.googleClientId) {
            console.warn('Google API key / client ID not set in cloudConfig.js');
            return null;
        }

        await this.loadScript('https://apis.google.com/js/api.js');
        await this.loadScript('https://accounts.google.com/gsi/client');

        //Obtain a short-lived OAuth access token via Google Identity Services.
        const token = await new Promise(resolve => {
            const client = google.accounts.oauth2.initTokenClient({
                client_id: c.googleClientId,
                scope: 'https://www.googleapis.com/auth/drive.readonly',
                callback: resp => {
                    if (resp && resp.access_token)
                        resolve(resp.access_token);
                    else
                        resolve(null);
                }
            });

            client.requestAccessToken();
        });

        if (!token)
            return null;

        await new Promise(resolve => gapi.load('picker', resolve));

        const apiKey = c.googleApiKey;
        return new Promise(resolve => {
            const view = new google.picker.DocsView(google.picker.ViewId.DOCS);
            if (filter === 'image')
                view.setMimeTypes('image/png,image/jpeg,image/svg+xml');

            const picker = new google.picker.PickerBuilder()
                .setOAuthToken(token)
                .setDeveloperKey(apiKey)
                .addView(view)
                .setCallback(data => {
                    if (data.action === google.picker.Action.PICKED)
                        resolve('https://drive.google.com/uc?export=download&id=' + data.docs[0].id);
                    else if (data.action === google.picker.Action.CANCEL)
                        resolve(null);
                })
                .build();

            picker.setVisible(true);
        });
    }
};
