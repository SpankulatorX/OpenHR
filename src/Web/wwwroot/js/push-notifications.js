// OpenHR Web Push — client helper (loaded by App.razor as window.OpenHR.push)
//
// Blazor Server architecture: the browser must NOT POST the subscription to an HTTP API.
// Instead, getSubscription() RETURNS the subscription to the Blazor component over the
// SignalR circuit (IJSRuntime), and the component persists it via IDbContextFactory.
//
// Push RECEIVING is handled by the already-registered root service worker
// (/service-worker.js, registered in App.razor), which has a 'push' + 'notificationclick'
// handler. When that worker is absent (e.g. a stripped deployment), we fall back to the
// dedicated /sw-push.js worker.

(function () {
    "use strict";

    window.OpenHR = window.OpenHR || {};

    window.OpenHR.push = {
        _registration: null,

        /** True when this browser can do Web Push at all. */
        isSupported: function () {
            return ("serviceWorker" in navigator) && ("PushManager" in window) && ("Notification" in window);
        },

        /** Current Notification permission: "granted" | "denied" | "default". */
        permission: function () {
            return ("Notification" in window) ? Notification.permission : "denied";
        },

        /**
         * Resolve a service worker registration usable for push. Prefers the root PWA
         * worker; registers /sw-push.js only if no root worker exists. Waits for it to
         * become active so pushManager is usable.
         */
        ensureRegistration: async function () {
            if (this._registration) {
                return this._registration;
            }
            if (!("serviceWorker" in navigator)) {
                return null;
            }

            let reg = await navigator.serviceWorker.getRegistration("/");
            if (!reg) {
                reg = await navigator.serviceWorker.register("/sw-push.js");
            }

            if (!reg.active) {
                await new Promise(function (resolve) {
                    const worker = reg.installing || reg.waiting;
                    if (!worker) {
                        resolve();
                        return;
                    }
                    worker.addEventListener("statechange", function () {
                        if (worker.state === "activated") {
                            resolve();
                        }
                    });
                    // Safety net so we never hang the circuit call.
                    setTimeout(resolve, 3000);
                });
            }

            this._registration = reg;
            return reg;
        },

        /** Ask for notification permission if not already decided. */
        requestPermission: async function () {
            if (!("Notification" in window)) {
                return "denied";
            }
            if (Notification.permission !== "default") {
                return Notification.permission;
            }
            return await Notification.requestPermission();
        },

        /**
         * Subscribe this device and return the subscription for the server to persist.
         * @param {string} vapidPublicKey base64url VAPID public key from the server.
         * @returns {Promise<{endpoint?:string,p256dh?:string,auth?:string,permission:string}|null>}
         *          Object with base64url keys on success, or {permission} when not granted.
         */
        getSubscription: async function (vapidPublicKey) {
            if (!this.isSupported()) {
                return null;
            }

            const permission = await this.requestPermission();
            if (permission !== "granted") {
                return { permission: permission };
            }

            const reg = await this.ensureRegistration();
            if (!reg) {
                return { permission: permission };
            }

            let subscription = await reg.pushManager.getSubscription();
            if (!subscription) {
                subscription = await reg.pushManager.subscribe({
                    userVisibleOnly: true,
                    applicationServerKey: this._urlBase64ToUint8Array(vapidPublicKey)
                });
            }

            // toJSON() returns the p256dh/auth keys already base64url-encoded, which is
            // exactly what the server-side WebPush library expects.
            const json = subscription.toJSON();
            const keys = json.keys || {};
            return {
                endpoint: subscription.endpoint,
                p256dh: keys.p256dh || "",
                auth: keys.auth || "",
                permission: "granted"
            };
        },

        /** True if a push subscription currently exists on this device. */
        isSubscribed: async function () {
            const reg = await this.ensureRegistration();
            if (!reg) {
                return false;
            }
            const subscription = await reg.pushManager.getSubscription();
            return subscription !== null;
        },

        /**
         * Remove this device's push subscription.
         * @returns {Promise<string|null>} The removed endpoint (so the server can deactivate it), or null.
         */
        removeSubscription: async function () {
            const reg = await this.ensureRegistration();
            if (!reg) {
                return null;
            }
            const subscription = await reg.pushManager.getSubscription();
            if (!subscription) {
                return null;
            }
            const endpoint = subscription.endpoint;
            await subscription.unsubscribe();
            return endpoint;
        },

        /** Convert a base64url VAPID key to the Uint8Array pushManager.subscribe expects. */
        _urlBase64ToUint8Array: function (base64String) {
            const padding = "=".repeat((4 - (base64String.length % 4)) % 4);
            const base64 = (base64String + padding).replace(/-/g, "+").replace(/_/g, "/");
            const rawData = window.atob(base64);
            const outputArray = new Uint8Array(rawData.length);
            for (let i = 0; i < rawData.length; ++i) {
                outputArray[i] = rawData.charCodeAt(i);
            }
            return outputArray;
        }
    };
})();
