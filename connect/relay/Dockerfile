FROM python:3.13-alpine
ARG FRP_VERSION=0.68.0
ARG FRP_SHA256=3cf934477f4fb1ee9e19e49c31fb33f5ffe3283300076f59afad8b8ccf1e1621
RUN apk add --no-cache openssl ca-certificates curl && \
    curl -fL --retry 3 "https://github.com/fatedier/frp/releases/download/v${FRP_VERSION}/frp_${FRP_VERSION}_linux_amd64.tar.gz" -o /tmp/frp.tar.gz && \
    echo "${FRP_SHA256}  /tmp/frp.tar.gz" | sha256sum -c - && \
    tar -xzf /tmp/frp.tar.gz -C /tmp && \
    cp "/tmp/frp_${FRP_VERSION}_linux_amd64/frps" /usr/local/bin/frps && \
    mkdir /licenses && cp "/tmp/frp_${FRP_VERSION}_linux_amd64/LICENSE" /licenses/frp-LICENSE && \
    rm -rf /tmp/frp*
WORKDIR /app
COPY admin.py run.py /app/
ENV PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1
CMD ["python", "/app/run.py"]
