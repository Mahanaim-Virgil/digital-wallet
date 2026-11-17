#!/bin/sh
# İki veritabanına bekleyen migration'ları uygular. Veritabanı yoksa EF oluşturur.
# Bağlantı dizeleri ortam değişkenlerinden (compose.yaml); şifre loglanmaz.
set -eu

echo "walletdb migration'ları uygulanıyor..."
/bundles/wallet-efbundle --connection "$WALLETDB_CONNECTION"

echo "notificationsdb migration'ları uygulanıyor..."
/bundles/notifications-efbundle --connection "$NOTIFICATIONSDB_CONNECTION"

echo "Migration'lar tamam."
