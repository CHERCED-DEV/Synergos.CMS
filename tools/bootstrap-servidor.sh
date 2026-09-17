#!/usr/bin/env bash
#
# Lo que se corre UNA VEZ en un servidor recién creado, como root.
#
#   scp tools/bootstrap-servidor.sh root@<ip>:/tmp/
#   ssh root@<ip> 'bash /tmp/bootstrap-servidor.sh'
#
# ─────────────────────────────────────────────────────────────────────────────
# ESTO NO DESPLIEGA NADA. Deja el servidor en el estado que el despliegue
# automático da por hecho: Docker instalado, un usuario que no es root, el
# directorio con los permisos correctos y el firewall cerrado.
#
# Está escrito para poder correrse DOS VECES sin romper nada — porque se va a
# correr dos veces. La primera siempre falta algo.
# ─────────────────────────────────────────────────────────────────────────────
#
# Lo que NO hace, a propósito:
#   · No escribe secretos. `.env` se rellena a mano; ver docs/despliegue.
#   · No abre 80/443 sólo a Cloudflare. Ese paso va DESPUÉS de que el DNS
#     apunte, o el sitio queda inalcanzable para uno mismo mientras se monta.

set -euo pipefail

USUARIO="${SYNERGOS_USER:-despliegue}"
DIR="/opt/synergos"

[ "$(id -u)" -eq 0 ] || { echo "✗ corré esto como root en el servidor recién creado."; exit 1; }

echo "══ 1. Paquetes base"
export DEBIAN_FRONTEND=noninteractive
apt-get update --quiet
# `age` y `rclone` son del respaldo (HU #31): cifrar en origen y llevárselo
# fuera. Van acá y no dentro de un contenedor a propósito — el respaldo tiene
# que poder correr aunque el stack esté caído, que es justo cuando hace falta.
apt-get install --yes --no-install-recommends ca-certificates curl gnupg ufw age rclone

echo "══ 2. Docker"
if command -v docker >/dev/null 2>&1; then
  echo "   ya está: $(docker --version)"
else
  # El repositorio oficial y no el `docker.io` de Ubuntu: el plugin `compose`
  # v2 —que es el que usa el despliegue— sólo viene por acá.
  install -m 0755 -d /etc/apt/keyrings
  curl --fail --silent --show-error --location https://download.docker.com/linux/ubuntu/gpg \
    | gpg --dearmor --yes --output /etc/apt/keyrings/docker.gpg
  chmod a+r /etc/apt/keyrings/docker.gpg

  echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] \
https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo "$VERSION_CODENAME") stable" \
    > /etc/apt/sources.list.d/docker.list

  apt-get update --quiet
  apt-get install --yes docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
  systemctl enable --now docker
fi

# Sin el plugin v2, `docker compose` (sin guion) no existe y el despliegue falla
# en el servidor con un mensaje que parece de otra cosa.
docker compose version >/dev/null 2>&1 || { echo "✗ falta el plugin docker-compose-v2"; exit 1; }

echo "══ 3. El usuario del despliegue"
#
# El action NO entra como root. No es ceremonia: la llave de despliegue vive en
# los secretos de GitHub, y una llave que abre root convierte cualquier fuga en
# el servidor entero en vez de en un directorio y un demonio.
if id "$USUARIO" >/dev/null 2>&1; then
  echo "   ya existe: $USUARIO"
else
  adduser --disabled-password --gecos "" "$USUARIO"
fi
usermod --append --groups docker "$USUARIO"

# La misma llave con la que entraste. Sin esto, el usuario nuevo existe y no hay
# forma de entrar con él — y el primer despliegue falla en `scp` sin decir por qué.
if [ -f /root/.ssh/authorized_keys ]; then
  install -o "$USUARIO" -g "$USUARIO" -m 700 -d "/home/$USUARIO/.ssh"
  install -o "$USUARIO" -g "$USUARIO" -m 600 /root/.ssh/authorized_keys "/home/$USUARIO/.ssh/authorized_keys"
  echo "   llaves copiadas desde root"
else
  echo "   ⚠ root no tiene authorized_keys: copiá tu llave pública a /home/$USUARIO/.ssh/ a mano"
fi

echo "══ 4. El directorio"
install -o "$USUARIO" -g "$USUARIO" -m 755 -d "$DIR"

if [ -f "$DIR/.env" ]; then
  echo "   .env ya existe — no se toca (tiene los secretos)"
else
  # Se deja el esqueleto con las llaves ya generadas: son las dos que se pueden
  # inventar acá sin pedirle nada a nadie, y las dos tienen que ser largas.
  cat > "$DIR/.env" <<ENV
# Rellenar y NO commitear. La plantilla comentada está en .env.example del repo.
SYNERGOS_REGISTRY=ghcr.io/cherced-dev
SYNERGOS_TAG=
SYNERGOS_DOMAIN=
SYNERGOS_API_KEY=$(openssl rand -hex 32)
SYNERGOS_CART_SECRET=$(openssl rand -hex 32)
SYNERGOS_CDN_MODE=Http
SYNERGOS_CDN_URL=https://synergos-ui.synergos-labs.workers.dev
Notifications__Resend__ApiKey=
Notifications__Resend__From=
Notifications__Resend__WebhookSecret=
ENV
  chown "$USUARIO:$USUARIO" "$DIR/.env"
  chmod 600 "$DIR/.env"
  echo "   .env creado con llaves nuevas. FALTA: SYNERGOS_DOMAIN y las de Resend."
fi

echo "══ 5. El respaldo, programado"
#
# Un script de respaldo que nadie corre es un fichero. Y sin corridas no hay
# retención: lo que borra las copias viejas es la corrida siguiente.
#
# 04:10 UTC porque `respaldo.sh` PARA LOS SERVICIOS para copiar en frío (el
# `lock` de proceso de JsonCollectionStore no admite copiar en caliente), así
# que esto es una caída corta programada, no una tarea invisible.
install -o "$USUARIO" -g "$USUARIO" -m 755 -d /var/backups/synergos
chmod 700 /var/backups/synergos   # datos personales: sólo el dueño

cat > /etc/cron.d/synergos-respaldo <<CRON
# Respaldo diario (HU #31). Lo deja en /var/backups/synergos y —si $DIR/.env
# tiene SYNERGOS_RESPALDO_DESTINO— se lo lleva fuera cifrado.
#
# Si el envío falla, esto termina en ERROR y cron manda el correo de la salida.
# Terminar en verde habiendo dejado la copia en el disco que venía a proteger
# es cómo se cree que hay respaldo sin tenerlo.
SHELL=/bin/bash
10 4 * * * $USUARIO $DIR/respaldo.sh >> /var/log/synergos-respaldo.log 2>&1
CRON
chmod 644 /etc/cron.d/synergos-respaldo
touch /var/log/synergos-respaldo.log
chown "$USUARIO:$USUARIO" /var/log/synergos-respaldo.log
echo "   diario a las 04:10 UTC → /var/backups/synergos"
echo "   ⚠ Sin SYNERGOS_RESPALDO_DESTINO en $DIR/.env eso NO sale del servidor,"
echo "     y una copia que muere con el disco no protege de perder el disco."

echo "══ 6. Firewall"
ufw allow 22/tcp   >/dev/null
ufw allow 80/tcp   >/dev/null
ufw allow 443/tcp  >/dev/null
ufw --force enable >/dev/null
echo "   22, 80 y 443 abiertos."
echo "   ⚠ Cerrar 80/443 a los rangos de Cloudflare va DESPUÉS de que el DNS apunte."

echo "══ 7. SSH sin contraseña"
sed -i 's/^#*PasswordAuthentication.*/PasswordAuthentication no/' /etc/ssh/sshd_config
systemctl reload ssh 2>/dev/null || systemctl reload sshd

echo
echo "✓ El servidor está listo. Lo que falta, y no lo puede hacer este script:"
echo "  1. Rellenar SYNERGOS_DOMAIN (y Resend) en $DIR/.env"
echo "  2. Los secretos en GitHub: DEPLOY_HOST=$(curl --silent --max-time 5 ifconfig.me || echo '<ip>')"
echo "     DEPLOY_USER=$USUARIO, DEPLOY_SSH_KEY=<la privada>"
echo "  3. La variable SYNERGOS_DOMAIN en GitHub (Variables, no Secrets)"
echo "  4. El DNS de Cloudflare apuntando acá, en naranja"
echo "  5. El respaldo FUERA de la máquina: SYNERGOS_RESPALDO_DESTINO y"
echo "     SYNERGOS_RESPALDO_LLAVE_PUBLICA en $DIR/.env (ver .env.example)."
echo "     Y después, el ensayo: tools/prueba-restauracion.sh — que NO se corre"
echo "     acá, porque la llave privada no vive en el servidor."
