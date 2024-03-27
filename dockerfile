FROM mcr.microsoft.com/dotnet/runtime:8.0

# Working directory anlegen, Dateien kopieren und Berechtigungen setzen
WORKDIR /app
COPY ./publish ./
RUN chmod +x "./isci.integration"

# Umgebungsvariablen setzen
ENV "ISCI_OrdnerAnwendungen"="/Anwendungen"
ENV "ISCI_OrdnerDatenstrukturen"="/Datenstrukturen"

# Umgebungsvariablen, die auf dem System angelegt werden müssen:
# ISCI_Identifikation=XXX
# ISCI_Ressource=XXX
# ISCI_Anwendung=XXX
#
# Die beiden Ordner in den Umgebungsvariablen vom Host-System müssen eingebunden werden
# Es muss außerdem eine Konfiguration ${ISCI_Identifikation}.json im Ordner "${ISCI_OrdnerAnwendungen}/${ISCI_Anwendung}/Konfigurationen" vorhanden sein
# 
# Ports die durchgereicht werden müssen:
# sendPort und receivePort also aktuell 5002 und 5003

ENTRYPOINT ["./isci.integration"]