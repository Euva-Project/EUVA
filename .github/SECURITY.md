
# Security Policy

## Supported Versions

As the project is in active development, security updates are currently prioritized for the latest builds.

Version >= 1.3-alpha - supported

---

## Reporting a Vulnerability

**EUVA** incorporates the **Yara-X** engine for malware analysis. We understand that security researchers may encounter vulnerabilities within the analyzer itself.

If you find a security-related bug e.g., a buffer overflow, a crash when parsing specific byte patterns, or a bypass of scanning logic, please follow these steps:

1. **Do not** create a public GitHub Issue immediately.
2. Report the vulnerability directly via:
   * **Email:** arsenija111mot@gmail.com
3. If you need to share a sample file that triggers the vulnerability, please send it in a **password-protected ZIP archive** (standard password: `infected`) to prevent accidental execution by mail servers or antivirus software.

We aim to acknowledge all reports within **48 hours** and will work with you to release a patch before the vulnerability is disclosed publicly.

---

## Malware Analysis Safety & Disclaimer

**EUVA** is a tool designed for reverse engineering unpacking, and security research. 

* **Isolate Your Environment:** Always perform analysis of live malware samples within a **disconnected Virtual Machine (VM)** or a dedicated sandbox.
* **No Liability:** The developers of EUVA are not responsible for any damage, data loss, or system infection caused by the execution or improper handling of malicious code while using this software.
* **Yara-X:** While the Yara-X engine provides memory safety, logic errors are still possible. Use with caution.

---
*This policy is part of the EUVA project commitment to the security community.*
