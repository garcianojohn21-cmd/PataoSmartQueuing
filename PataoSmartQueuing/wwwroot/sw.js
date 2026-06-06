// wwwroot/sw.js - Service Worker for Push Notifications

self.addEventListener('install', (event) => {
    console.log('✅ Service Worker installed');
    self.skipWaiting();
});

self.addEventListener('activate', (event) => {
    console.log('✅ Service Worker activated');
    event.waitUntil(clients.claim());
});

// Listen for push events
self.addEventListener('push', (event) => {
    console.log('📨 Push notification received');

    let data = {
        title: 'Queue Notification',
        message: 'You have a queue update',
        type: 'info'
    };

    if (event.data) {
        try {
            data = event.data.json();
        } catch (e) {
            data.message = event.data.text();
        }
    }

    const options = {
        body: data.message,
        icon: '/images/notification-icon.png',
        badge: '/images/badge-icon.png',
        vibrate: [200, 100, 200],
        tag: 'queue-notification-' + Date.now(),
        requireInteraction: data.type === 'warning' || data.type === 'error',
        data: {
            url: '/Student/Dashboard',
            type: data.type
        },
        actions: [
            {
                action: 'view',
                title: 'View Dashboard'
            },
            {
                action: 'dismiss',
                title: 'Dismiss'
            }
        ]
    };

    event.waitUntil(
        self.registration.showNotification(data.title, options)
    );
});

// Handle notification clicks
self.addEventListener('notificationclick', (event) => {
    console.log('🔔 Notification clicked:', event.action);

    event.notification.close();

    if (event.action === 'view' || event.action === '') {
        event.waitUntil(
            clients.matchAll({ type: 'window', includeUncontrolled: true })
                .then((clientList) => {
                    for (let client of clientList) {
                        if (client.url.includes('/Student/Dashboard') && 'focus' in client) {
                            return client.focus();
                        }
                    }
                    if (clients.openWindow) {
                        return clients.openWindow('/Student/Dashboard');
                    }
                })
        );
    }
});

self.addEventListener('notificationclose', (event) => {
    console.log('Notification closed:', event.notification.tag);
});