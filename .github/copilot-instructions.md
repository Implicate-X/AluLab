# Copilot Instructions

## Project Guidelines
- Architekturregel: Pin-Belegungen (z.B. DC/RESET/LED sowie Expander-INT/RESET) dürfen nicht in `Board` hardcodiert sein, sondern müssen aus dem jeweiligen `HardwareContext` (Workbench: FTDI, Gateway: Raspberry Pi) bereitgestellt werden; kein GPIO-Pin-Mapping als Workaround im Gateway.