FROM postgres:16-bookworm
RUN apt-get update && apt-get install -y --no-install-recommends age bash findutils coreutils ca-certificates && rm -rf /var/lib/apt/lists/*
COPY deploy/ /opt/deploy/
WORKDIR /opt
USER postgres
ENTRYPOINT ["bash", "/opt/deploy/backup.sh"]
