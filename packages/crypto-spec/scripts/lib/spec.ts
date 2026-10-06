import { readFileSync } from 'node:fs';

import { fromRoot, repoRoot, specPath } from './paths.ts';

/** The shape of `crypto-spec.json`. */
export interface CryptoSpec {
  protocolMajor: number;
  keyHierarchy: {
    rootKeyLength: number;
    deviceKey: { algorithm: string; construction: string; label: string; length: number };
    purposeKeys: {
      algorithm: string;
      construction: string;
      length: number;
      labels: { seal: string; ack: string; hubAuth: string };
    };
    deviceId: { algorithm: string; construction: string; label: string; length: number };
  };
  aead: { algorithm: string; keyLength: number; nonceLength: number; tagLength: number };
  frame: {
    nonceLayout: string;
    nonceDeviceIdPrefixLength: number;
    aadLayout: string;
    aadLength: number;
    replayWindow: number;
    keys: string;
  };
  enrolment: {
    hpke: {
      mode: number;
      kemId: number;
      kdfId: number;
      aeadId: number;
      versionLabel: string;
      labels: {
        dkpPrk: string;
        sk: string;
        eaePrk: string;
        sharedSecret: string;
        pskIdHash: string;
        infoHash: string;
        secret: string;
        key: string;
        baseNonce: string;
      };
    };
    construction: string;
    info: string;
    x25519KeyLength: number;
    encLength: number;
    fingerprint: string;
  };
  setup: {
    construction: string;
    label: string;
    okmLength: number;
    appToHubKeyOffset: number;
    hubToAppKeyOffset: number;
    keyLength: number;
    maxCodeLength: number;
    code: string;
    nonceLayout: string;
    noncePrefixLength: number;
    firstCounter: number;
    aadLayout: string;
    wrongCode: string;
  };
  heartbeat: {
    algorithm: string;
    key: string;
    canonical: string;
    separator: string;
    signature: string;
    headers: { device: string; timestamp: string; nonce: string; signature: string };
    nonceLength: number;
    maxSkewMs: number;
  };
  sensorId: {
    algorithm: string;
    construction: string;
    urlNamespace: string;
    namespaceName: string;
    namespace: string;
    nameFormat: string;
    name: string;
    quantities: { soilMoisture: string; airTemperature: string; relativeHumidity: string; gasResistance: string };
    deviceReport: string;
    deviceReportUse: string;
  };
}

export function loadSpec(root = repoRoot): CryptoSpec {
  return JSON.parse(readFileSync(fromRoot(specPath, root), 'utf8')) as CryptoSpec;
}

/**
 * How a constant is typed in each language: a string, a byte length (`usize` / `int` / `Int`), a
 * byte, a 16-bit algorithm identifier, a 64-bit counter, or a duration in milliseconds.
 */
export type ConstantKind = 'str' | 'len' | 'u8' | 'u16' | 'u64' | 'ms';

export interface Constant {
  /** camelCase; rendered SCREAMING_SNAKE in Rust and Kotlin, PascalCase in C#. */
  name: string;
  kind: ConstantKind;
  value: string | number;
  doc: string;
}

export interface ConstantGroup {
  title: string;
  constants: Constant[];
}

/** Every generated constant, grouped as the spec groups them. */
export function constantsOf(spec: CryptoSpec): ConstantGroup[] {
  const { keyHierarchy: keys, aead, frame, enrolment, setup, heartbeat, sensorId } = spec;
  const hpke = enrolment.hpke;
  return [
    {
      title: 'Protocol',
      constants: [
        { name: 'protocolMajor', kind: 'u8', value: spec.protocolMajor, doc: 'The current wire major; every frame and envelope carries it.' },
      ],
    },
    {
      title: 'Key hierarchy (AD-12)',
      constants: [
        { name: 'rootKeyLength', kind: 'len', value: keys.rootKeyLength, doc: 'Length of the eFuse root key in bytes.' },
        { name: 'deviceKeyLabel', kind: 'str', value: keys.deviceKey.label, doc: `K_dev message: \`${keys.deviceKey.construction}\`.` },
        { name: 'deviceKeyLength', kind: 'len', value: keys.deviceKey.length, doc: 'Length of K_dev in bytes.' },
        { name: 'purposeKeyLength', kind: 'len', value: keys.purposeKeys.length, doc: `Length of a purpose key in bytes: \`${keys.purposeKeys.construction}\`.` },
        { name: 'sealLabel', kind: 'str', value: keys.purposeKeys.labels.seal, doc: 'HKDF info of the uplink (frame) sealing key.' },
        { name: 'ackLabel', kind: 'str', value: keys.purposeKeys.labels.ack, doc: 'HKDF info of the downlink (acknowledgement) sealing key.' },
        { name: 'hubAuthLabel', kind: 'str', value: keys.purposeKeys.labels.hubAuth, doc: 'HKDF info of the Hub request authentication key.' },
        { name: 'deviceIdLabel', kind: 'str', value: keys.deviceId.label, doc: `HKDF info of the Device ID: \`${keys.deviceId.construction}\`.` },
        { name: 'deviceIdLength', kind: 'len', value: keys.deviceId.length, doc: 'Length of the Device ID in bytes.' },
        { name: 'deviceIdHexLength', kind: 'len', value: keys.deviceId.length * 2, doc: 'Length of the Device ID text form (lowercase hex).' },
      ],
    },
    {
      title: `AEAD (${aead.algorithm})`,
      constants: [
        { name: 'aeadKeyLength', kind: 'len', value: aead.keyLength, doc: 'AEAD key length in bytes.' },
        { name: 'aeadNonceLength', kind: 'len', value: aead.nonceLength, doc: 'AEAD nonce length in bytes.' },
        { name: 'aeadTagLength', kind: 'len', value: aead.tagLength, doc: 'AEAD tag length in bytes; a ciphertext is the encrypted bytes followed by the tag.' },
      ],
    },
    {
      title: 'Frames and downlinks (AD-12, AD-17)',
      constants: [
        { name: 'frameNonceDeviceIdPrefixLength', kind: 'len', value: frame.nonceDeviceIdPrefixLength, doc: `Device ID bytes at the start of the nonce: \`${frame.nonceLayout}\`.` },
        { name: 'frameAadLength', kind: 'len', value: frame.aadLength, doc: `Frame AAD length in bytes: \`${frame.aadLayout}\`.` },
        { name: 'replayWindow', kind: 'len', value: frame.replayWindow, doc: 'Counters below the high-water mark that stay acceptable once each.' },
      ],
    },
    {
      title: 'Enrolment (HPKE, RFC 9180)',
      constants: [
        { name: 'hpkeMode', kind: 'u8', value: hpke.mode, doc: 'HPKE mode: base.' },
        { name: 'hpkeKemId', kind: 'u16', value: hpke.kemId, doc: 'HPKE KEM: DHKEM(X25519, HKDF-SHA256).' },
        { name: 'hpkeKdfId', kind: 'u16', value: hpke.kdfId, doc: 'HPKE KDF: HKDF-SHA256.' },
        { name: 'hpkeAeadId', kind: 'u16', value: hpke.aeadId, doc: 'HPKE AEAD: ChaCha20Poly1305.' },
        { name: 'hpkeVersionLabel', kind: 'str', value: hpke.versionLabel, doc: 'RFC 9180 labeled-KDF prefix.' },
        { name: 'hpkeLabelDkpPrk', kind: 'str', value: hpke.labels.dkpPrk, doc: 'RFC 9180 DeriveKeyPair extract label.' },
        { name: 'hpkeLabelSk', kind: 'str', value: hpke.labels.sk, doc: 'RFC 9180 DeriveKeyPair expand label.' },
        { name: 'hpkeLabelEaePrk', kind: 'str', value: hpke.labels.eaePrk, doc: 'RFC 9180 ExtractAndExpand extract label.' },
        { name: 'hpkeLabelSharedSecret', kind: 'str', value: hpke.labels.sharedSecret, doc: 'RFC 9180 ExtractAndExpand expand label.' },
        { name: 'hpkeLabelPskIdHash', kind: 'str', value: hpke.labels.pskIdHash, doc: 'RFC 9180 key schedule label.' },
        { name: 'hpkeLabelInfoHash', kind: 'str', value: hpke.labels.infoHash, doc: 'RFC 9180 key schedule label.' },
        { name: 'hpkeLabelSecret', kind: 'str', value: hpke.labels.secret, doc: 'RFC 9180 key schedule label.' },
        { name: 'hpkeLabelKey', kind: 'str', value: hpke.labels.key, doc: 'RFC 9180 key schedule label.' },
        { name: 'hpkeLabelBaseNonce', kind: 'str', value: hpke.labels.baseNonce, doc: 'RFC 9180 key schedule label.' },
        { name: 'enrolmentInfo', kind: 'str', value: enrolment.info, doc: `HPKE info of the enrolment: \`${enrolment.construction}\`.` },
        { name: 'x25519KeyLength', kind: 'len', value: enrolment.x25519KeyLength, doc: 'Length of a raw X25519 public or private key in bytes.' },
        { name: 'hpkeEncLength', kind: 'len', value: enrolment.encLength, doc: 'Length of the HPKE encapsulated key in bytes.' },
      ],
    },
    {
      title: 'BLE setup session (AD-25)',
      constants: [
        { name: 'setupLabel', kind: 'str', value: setup.label, doc: `Setup-session HKDF info prefix: \`${setup.construction}\`.` },
        { name: 'setupOkmLength', kind: 'len', value: setup.okmLength, doc: 'Setup-session HKDF output length in bytes.' },
        { name: 'setupKeyLength', kind: 'len', value: setup.keyLength, doc: 'Length of each direction key in bytes.' },
        { name: 'setupMaxCodeLength', kind: 'len', value: setup.maxCodeLength, doc: `Longest PoP code accepted, in characters: ${setup.code}.` },
        { name: 'setupAppToHubKeyOffset', kind: 'len', value: setup.appToHubKeyOffset, doc: 'Offset of the app-to-Hub key in the HKDF output.' },
        { name: 'setupHubToAppKeyOffset', kind: 'len', value: setup.hubToAppKeyOffset, doc: 'Offset of the Hub-to-app key in the HKDF output.' },
        { name: 'setupNoncePrefixLength', kind: 'len', value: setup.noncePrefixLength, doc: `Zero bytes at the start of the nonce: \`${setup.nonceLayout}\`.` },
        { name: 'setupFirstCounter', kind: 'u64', value: setup.firstCounter, doc: 'Counter of the first message in each direction.' },
      ],
    },
    {
      title: 'Heartbeat authentication (AD-12)',
      constants: [
        { name: 'heartbeatSeparator', kind: 'str', value: heartbeat.separator, doc: `Separator of the canonical string: \`${heartbeat.canonical}\`.` },
        { name: 'heartbeatDeviceHeader', kind: 'str', value: heartbeat.headers.device, doc: 'Header carrying the Device ID (lowercase hex).' },
        { name: 'heartbeatTimestampHeader', kind: 'str', value: heartbeat.headers.timestamp, doc: 'Header carrying the request time in Unix milliseconds.' },
        { name: 'heartbeatNonceHeader', kind: 'str', value: heartbeat.headers.nonce, doc: 'Header carrying the request nonce (lowercase hex).' },
        { name: 'heartbeatSignatureHeader', kind: 'str', value: heartbeat.headers.signature, doc: `Header carrying the signature: \`${heartbeat.signature}\`.` },
        { name: 'heartbeatNonceLength', kind: 'len', value: heartbeat.nonceLength, doc: 'Length of the request nonce in bytes.' },
        { name: 'heartbeatMaxSkewMs', kind: 'ms', value: heartbeat.maxSkewMs, doc: 'Largest accepted difference between the request time and the Server clock, in ms.' },
      ],
    },
    {
      title: 'Sensor identity (AD-19)',
      constants: [
        { name: 'sensorIdNamespace', kind: 'str', value: sensorId.namespace, doc: `UUIDv5 namespace of every Sensor ID, itself UUIDv5(URL namespace, "${sensorId.namespaceName}"): \`${sensorId.construction}\`.` },
        { name: 'sensorIdNameFormat', kind: 'str', value: sensorId.nameFormat, doc: `The UUIDv5 name of a Sensor: ${sensorId.name}.` },
        { name: 'sensorQuantitySoilMoisture', kind: 'str', value: sensorId.quantities.soilMoisture, doc: 'Quantity token of soil moisture.' },
        { name: 'sensorQuantityAirTemperature', kind: 'str', value: sensorId.quantities.airTemperature, doc: 'Quantity token of air temperature.' },
        { name: 'sensorQuantityRelativeHumidity', kind: 'str', value: sensorId.quantities.relativeHumidity, doc: 'Quantity token of relative humidity.' },
        { name: 'sensorQuantityGasResistance', kind: 'str', value: sensorId.quantities.gasResistance, doc: 'Quantity token of raw gas resistance.' },
        { name: 'deviceReportSensorId', kind: 'str', value: sensorId.deviceReport, doc: `Stands in for the Sensor ID where a device report is keyed: ${sensorId.deviceReportUse}.` },
      ],
    },
  ];
}
