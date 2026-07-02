// OpenHR — dedicated Web Push service worker.
//
// The full PWA worker (/service-worker.js, registered in App.razor) already handles push
// and is the ACTIVE receiver in the standard deployment. This file is a standalone,
// push-only worker used as a fallback by push-notifications.js when no root worker is
// registered (e.g. a stripped-down deployment). It intentionally implements ONLY the
// 'push' and 'notificationclick' handlers — no caching / offline logic.
//
// Payload shape (produced by RegionHR.Notifications.Domain.PushPayload):
//   { title, body, url, tag, icon, requireInteraction }

self.addEventListener("install", function () {
    self.skipWaiting();
});

self.addEventListener("activate", function (event) {
    event.waitUntil(self.clients.claim());
});

self.addEventListener("push", function (event) {
    let data = { title: "OpenHR", body: "Ny notis", icon: "/favicon.png" };

    if (event.data) {
        try {
            data = Object.assign(data, event.data.json());
        } catch (e) {
            data.body = event.data.text();
        }
    }

    const options = {
        body: data.body,
        icon: data.icon || "/favicon.png",
        badge: "/favicon.png",
        tag: data.tag || "openhr-notification",
        data: { url: data.url || "/notiser" },
        vibrate: [100, 50, 100],
        requireInteraction: data.requireInteraction || false
    };

    event.waitUntil(self.registration.showNotification(data.title, options));
});

self.addEventListener("notificationclick", function (event) {
    event.notification.close();
    const url = (event.notification.data && event.notification.data.url) || "/notiser";

    event.waitUntil(
        self.clients.matchAll({ type: "window", includeUncontrolled: true }).then(function (clientList) {
            for (const client of clientList) {
                if (client.url.indexOf(self.location.origin) === 0 && "focus" in client) {
                    if ("navigate" in client) {
                        client.navigate(url);
                    }
                    return client.focus();
                }
            }
            return self.clients.openWindow(url);
        })
    );
});
