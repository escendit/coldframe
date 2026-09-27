# Spike trust anchors

`roots.pem` is the public-root bundle the spike Hub embeds (AD-13: the Hub trusts
public roots only). Extracted from the Fedora system trust store on 2026-09-27.

| Root | Why | Key | Expires | SHA-256 fingerprint |
| --- | --- | --- | --- | --- |
| ISRG Root X1 | Let's Encrypt RSA chains (the real Coldframe Server, AD-13) | RSA 4096 | 2035-06-04 | `96:BC:EC:06:26:49:76:F3:74:60:77:9A:CF:28:C5:A7:CF:E8:A3:C0:AA:E1:1A:8F:FC:EE:05:C0:BD:DF:08:C6` |
| ISRG Root X2 | Let's Encrypt ECDSA chains | EC P-384 | 2040-09-17 | `69:72:9B:8E:15:A8:6E:FC:17:7A:57:AF:B7:17:1D:FC:64:AD:D2:8C:2F:CA:8C:F1:50:7E:34:45:3C:CB:14:70` |
| SSL.com TLS ECC Root CA 2022 | `example.com` (default `SPIKE_URL`) is served via Cloudflare TLS Issuing ECC CA 3 → SSL.com TLS Transit ECC CA R2 → this root | EC P-384 | 2046-08-19 | `C3:2F:FD:9F:46:F9:36:D1:6C:36:73:99:09:59:43:4B:9A:D6:0A:AF:BB:9E:7C:F3:36:54:F1:44:CC:1B:A1:43` |
| DigiCert Global Root G2 | Common fallback for other test URLs | RSA 2048 | 2038-01-15 | `CB:3C:CB:B7:60:31:E5:E0:13:8F:8D:D3:9A:23:F9:DE:47:FF:C3:5E:43:C1:14:4C:EA:27:D4:6A:5A:B1:CB:5F` |

If you point `SPIKE_URL` at a host whose chain ends elsewhere, append that root
(PEM) to `roots.pem` and rebuild. Check a chain with:

```sh
openssl s_client -connect HOST:443 -servername HOST -showcerts </dev/null | grep -E ' s:| i:'
```
