#!/usr/bin/env bash
# docker compose ile ayağa kalkmış sistemin duman testi (yerelde ve CI'da).
# Akış: iki cüzdan → para yatırma → transfer → olaylar RabbitMQ üzerinden bildirim servisine ulaşmalı.
# Kullanım: bash scripts/smoke-test.sh [api_adresi] [bildirim_adresi]
set -euo pipefail

API="${1:-http://localhost:8080}"
NOTIFICATIONS="${2:-http://localhost:8081}"
J="Content-Type: application/json"
RUN="$(date +%s)"

fail() { echo "BAŞARISIZ: $*" >&2; exit 1; }
id_of() { sed -E 's/.*"id":"([^"]+)".*/\1/'; }

echo "API bekleniyor: $API"
for _ in $(seq 1 60); do
  curl -s -m 3 -o /dev/null "$API/wallets/00000000-0000-0000-0000-000000000949" && break
  sleep 2
done

ALI=$(curl -sf -m 10 -X POST "$API/wallets" -H "$J" -d '{"ownerName":"Duman Ali","currency":"TRY"}' | id_of) || fail "cüzdan açılamadı"
AYSE=$(curl -sf -m 10 -X POST "$API/wallets" -H "$J" -d '{"ownerName":"Duman Ayse","currency":"TRY"}' | id_of) || fail "cüzdan açılamadı"

code=$(curl -s -m 10 -o /dev/null -w '%{http_code}' -X POST "$API/wallets/$ALI/deposits" -H "$J" \
  -H "Idempotency-Key: smoke-dep-$RUN" -d '{"amount":100}')
[ "$code" = "201" ] || fail "para yatırma HTTP $code"

code=$(curl -s -m 10 -o /dev/null -w '%{http_code}' -X POST "$API/transfers" -H "$J" \
  -H "Idempotency-Key: smoke-tr-$RUN" -d "{\"fromWalletId\":\"$ALI\",\"toWalletId\":\"$AYSE\",\"amount\":40}")
[ "$code" = "201" ] || fail "transfer HTTP $code"

balance=$(curl -sf -m 10 "$API/wallets/$ALI" | sed -E 's/.*"balance":([^,]+),.*/\1/')
[ "$balance" = "60.00" ] || fail "Ali'nin bakiyesi 60.00 değil: $balance"

echo "Bildirimler bekleniyor (outbox → RabbitMQ → bildirim servisi)..."
for _ in $(seq 1 30); do
  ali_count=$(curl -s -m 5 "$NOTIFICATIONS/wallets/$ALI/notifications" | grep -o '"message"' | wc -l || true)
  ayse_count=$(curl -s -m 5 "$NOTIFICATIONS/wallets/$AYSE/notifications" | grep -o '"message"' | wc -l || true)
  if [ "$ali_count" -ge 2 ] && [ "$ayse_count" -ge 1 ]; then
    echo "TAMAM: bakiye $balance, Ali $ali_count bildirim, Ayşe $ayse_count bildirim."
    exit 0
  fi
  sleep 2
done
fail "bildirimler gelmedi (Ali: ${ali_count:-0}/2, Ayşe: ${ayse_count:-0}/1)"
