# Copilot Instructions

## Project Guidelines
- In the ALU UI, pins M and CN are externally controlled read-only inputs; the OperationSelector must treat CN as read-only (only read/switch list by CN, never set CN from Operation selection). 
- Operation selection must not set pins M and CN. Only S0..S3 may be set as an exception from the selector, and changes to S0..S3 must update the selector selection.