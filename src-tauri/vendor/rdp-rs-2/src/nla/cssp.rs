use nla::asn1::{ASN1, Sequence, ExplicitTag, SequenceOf, ASN1Type, OctetString, Integer, to_der};
use model::error::{RdpError, RdpErrorKind, Error, RdpResult};
use num_bigint::{BigUint};
use yasna::Tag;
use x509_parser::{parse_x509_der, X509Certificate};
use nla::sspi::AuthenticationProtocol;
use model::link::Link;
use std::io::{Read, Write};

/// Create a ts request as expected by the specification
/// https://docs.microsoft.com/en-us/openspecs/windows_protocols/ms-cssp/6aac4dea-08ef-47a6-8747-22ea7f6d8685?redirectedfrom=MSDN
///
/// This is the first payload sent from client to server
///
/// # Example
/// ```
/// use rdp::nla::cssp::create_ts_request;
/// let payload = create_ts_request(vec![0, 1, 2]);
/// assert_eq!(payload, [48, 18, 160, 3, 2, 1, 2, 161, 11, 48, 9, 48, 7, 160, 5, 4, 3, 0, 1, 2])
/// ```
pub fn create_ts_request(nego: Vec<u8>) -> Vec<u8> {
    let ts_request = sequence![
        "version" => ExplicitTag::new(Tag::context(0), 2 as Integer),
        "negoTokens" => ExplicitTag::new(Tag::context(1),
            sequence_of![
                sequence![
                    "negoToken" => ExplicitTag::new(Tag::context(0), nego)
                ]
            ])
    ];
    to_der(&ts_request)
}

/// This is the second step in CSSP handshake
/// this is the challenge message from server to client
///
/// This function will parse the request and extract the negoToken
/// which use as payload field
///
/// https://docs.microsoft.com/en-us/openspecs/windows_protocols/ms-cssp/6aac4dea-08ef-47a6-8747-22ea7f6d8685?redirectedfrom=MSDN
/// https://docs.microsoft.com/en-us/openspecs/windows_protocols/ms-cssp/9664994d-0784-4659-b85b-83b8d54c2336
/// 
/// # Example
/// ```
/// use rdp::nla::cssp::read_ts_server_challenge;
/// let challenge = [48, 18, 160, 3, 2, 1, 2, 161, 11, 48, 9, 48, 7, 160, 5, 4, 3, 0, 1, 2];
/// let payload = read_ts_server_challenge(&challenge).unwrap();
/// assert_eq!(payload, [0, 1, 2])
/// ```
pub fn read_ts_server_challenge(stream: &[u8]) -> RdpResult<Vec<u8>> {
    let mut ts_request = sequence![
        "version" => ExplicitTag::new(Tag::context(0), 2 as Integer),
        "negoTokens" => ExplicitTag::new(Tag::context(1),
            SequenceOf::reader(|| {
                Box::new(sequence![
                    "negoToken" => ExplicitTag::new(Tag::context(0), OctetString::new())
                ])
            })
         )
    ];

    yasna::parse_der(stream, |reader| {
        if let Err(Error::ASN1Error(e)) = ts_request.read_asn1(reader) {
            return Err(e)
        }
        Ok(())
    })?;

    let nego_tokens = cast!(ASN1Type::SequenceOf, ts_request["negoTokens"]).unwrap();
    let first_nego_tokens = cast!(ASN1Type::Sequence, nego_tokens.inner[0]).unwrap();
    let nego_token = cast!(ASN1Type::OctetString, first_nego_tokens["negoToken"]).unwrap();
    Ok(nego_token.to_vec())
}

/// This the third step in CSSP Handshake
/// Send the pubKey of server encoded with negotiated key
/// to protect agains MITM attack
///
/// https://docs.microsoft.com/en-us/openspecs/windows_protocols/ms-cssp/6aac4dea-08ef-47a6-8747-22ea7f6d8685?redirectedfrom=MSDN
///
/// # Example
/// ```
/// use rdp::nla::cssp::create_ts_authenticate;
/// let payload = create_ts_authenticate(vec![0, 1, 2], vec![0, 1, 2]);
/// assert_eq!(payload, [48, 25, 160, 3, 2, 1, 2, 161, 11, 48, 9, 48, 7, 160, 5, 4, 3, 0, 1, 2, 163, 5, 4, 3, 0, 1, 2])
/// ```
pub fn create_ts_authenticate(nego: Vec<u8>, pub_key_auth: Vec<u8>) -> Vec<u8> {
    let ts_challenge = sequence![
        "version" => ExplicitTag::new(Tag::context(0), 2 as Integer),
        "negoTokens" => ExplicitTag::new(Tag::context(1),
            sequence_of![
                sequence![
                    "negoToken" => ExplicitTag::new(Tag::context(0), nego as OctetString)
                ]
            ]),
        "pubKeyAuth" => ExplicitTag::new(Tag::context(3), pub_key_auth as OctetString)
    ];

    to_der(&ts_challenge)
}

fn get_der_length(der: &[u8]) -> Option<usize> {
    if der.len() < 2 {
        return None;
    }
    let length_byte = der[1];
    if length_byte & 0x80 == 0 {
        // Short form
        let len = length_byte as usize;
        let total_len = 2 + len;
        if total_len <= der.len() {
            Some(total_len)
        } else {
            None
        }
    } else {
        // Long form
        let len_bytes_count = (length_byte & 0x7f) as usize;
        if der.len() < 2 + len_bytes_count {
            return None;
        }
        let mut len = 0;
        for i in 0..len_bytes_count {
            len = (len << 8) | (der[2 + i] as usize);
        }
        let total_len = 2 + len_bytes_count + len;
        if total_len <= der.len() {
            Some(total_len)
        } else {
            None
        }
    }
}

fn extract_public_key_from_der_yasna(der: &[u8]) -> Result<Vec<u8>, yasna::ASN1Error> {
    let sliced_der = if let Some(len) = get_der_length(der) {
        &der[..len]
    } else {
        der
    };
    yasna::parse_der(sliced_der, |reader| {
        reader.read_sequence(|reader| {
            // TBSCertificate SEQUENCE is the first field of the outer Certificate SEQUENCE
            let pub_key_bytes = reader.next().read_sequence(|reader| {
                // 1. version [0] EXPLICIT Version DEFAULT v1 OPTIONAL
                let _version: Option<i64> = reader.read_optional(|reader| {
                    reader.read_tagged(yasna::Tag::context(0), |reader| {
                        reader.read_i64()
                    })
                })?;

                // 2. serialNumber (Integer)
                let _serial = reader.next().read_der()?;

                // 3. signature (AlgorithmIdentifier SEQUENCE)
                let _sig = reader.next().read_der()?;

                // 4. issuer (Name SEQUENCE)
                let _issuer = reader.next().read_der()?;

                // 5. validity (Validity SEQUENCE)
                let _validity = reader.next().read_der()?;

                // 6. subject (Name SEQUENCE)
                let _subject = reader.next().read_der()?;

                // 7. subjectPublicKeyInfo (SEQUENCE)
                let pub_key_bytes = reader.next().read_sequence(|reader| {
                    // algorithm (AlgorithmIdentifier)
                    let _alg = reader.next().read_der()?;
                    // subjectPublicKey (BIT STRING)
                    let (bit_string_bytes, _) = reader.next().read_bitvec_bytes()?;
                    Ok(bit_string_bytes)
                })?;

                // Consume any remaining fields in TBSCertificate (like extensions [3])
                while let Ok(_) = reader.next().read_der() {}

                Ok(pub_key_bytes)
            })?;

            // Consume any remaining fields in Certificate (like signatureAlgorithm and signatureValue)
            while let Ok(_) = reader.next().read_der() {}

            Ok(pub_key_bytes)
        })
    })
}

pub fn read_public_certificate(stream: &[u8]) -> RdpResult<X509Certificate> {
    let (_, cert) = parse_x509_der(stream).map_err(|e| RdpError::new(RdpErrorKind::InvalidData, &format!("Failed to parse server certificate: {:?}", e)))?;
    Ok(cert)
}

/// read ts validate
/// This is the last step in cssp handshake
/// Server must send its public key incremented by one
/// and cyphered with the authentication protocol
/// Parse the ts message and extract the public key
///
/// # Example
/// ```
/// use rdp::nla::cssp::read_ts_validate;
/// let pub_key = read_ts_validate(&[48, 12, 160, 3, 2, 1, 2, 163, 5, 4, 3, 0, 1, 2]).unwrap();
/// assert_eq!(pub_key, [0, 1, 2])
/// ```
pub fn read_ts_validate(request: &[u8]) -> RdpResult<Vec<u8>> {
    let mut ts_challenge = sequence![
        "version" => ExplicitTag::new(Tag::context(0), 2 as Integer),
        "pubKeyAuth" => ExplicitTag::new(Tag::context(3), OctetString::new())
    ];

    yasna::parse_der(request, |reader| {
        if let Err(Error::ASN1Error(e)) = ts_challenge.read_asn1(reader) {
            return Err(e)
        }
        Ok(())
    })?;
    let pubkey = cast!(ASN1Type::OctetString, ts_challenge["pubKeyAuth"])?;
    Ok(pubkey.to_vec())
}

fn create_ts_credentials(domain: Vec<u8>, user: Vec<u8>, password: Vec<u8>) -> Vec<u8> {
    let ts_password_creds = sequence![
        "domainName" => ExplicitTag::new(Tag::context(0), domain as OctetString),
        "userName" => ExplicitTag::new(Tag::context(1), user as OctetString),
        "password" => ExplicitTag::new(Tag::context(2), password as OctetString)
    ];

    let ts_password_cred_encoded = yasna::construct_der(|writer| {
        ts_password_creds.write_asn1(writer).unwrap();
    });

    let ts_credentials = sequence![
        "credType" => ExplicitTag::new(Tag::context(0), 1 as Integer),
        "credentials" => ExplicitTag::new(Tag::context(1), ts_password_cred_encoded as OctetString)
    ];

    to_der(&ts_credentials)
}

fn create_ts_authinfo(auth_info: Vec<u8>) -> Vec<u8> {
    let ts_authinfo = sequence![
        "version" => ExplicitTag::new(Tag::context(0), 2 as Integer),
        "authInfo" => ExplicitTag::new(Tag::context(2), auth_info)
    ];

    to_der(&ts_authinfo)
}

/// This the main function for CSSP protocol
/// It will use the raw link layer and the selected authenticate protocol
/// to perform the NLA authenticate
pub fn cssp_connect<S: Read + Write>(link: &mut Link<S>, authentication_protocol: &mut dyn AuthenticationProtocol, restricted_admin_mode: bool) -> RdpResult<()> {
    // first step is to send the negotiate message from authentication protocol
    let negotiate_message = create_ts_request(authentication_protocol.create_negotiate_message()?);
    link.write(&negotiate_message)?;

    // now receive server challenge
    let server_challenge = read_ts_server_challenge(&(link.read(0)?))?;

    // now ask for to authenticate protocol
    let client_challenge = authentication_protocol.read_challenge_message(&server_challenge)?;

    // now we need to build the security interface for auth protocol
    let mut security_interface = authentication_protocol.build_security_interface();

    // Get the peer public certificate
    let certificate_der = try_option!(link.get_peer_certificate()?, "No public certificate available")?;
    
    // Attempt to parse using yasna first (since x509-parser is strict and fails on many custom certificates)
    let public_key_data = match extract_public_key_from_der_yasna(&certificate_der) {
        Ok(key) => key,
        Err(yasna_err) => {
            // Fallback to original x509-parser
            let certificate = read_public_certificate(&certificate_der).map_err(|e| {
                RdpError::new(
                    RdpErrorKind::InvalidData,
                    &format!("Failed to parse server certificate. Yasna error: {:?}, Parser error: {:?}", yasna_err, e),
                )
            })?;
            certificate.tbs_certificate.subject_pki.subject_public_key.data.to_vec()
        }
    };

    // Now we can send back our challenge payload wit the public key encoded
    let challenge = create_ts_authenticate(client_challenge, security_interface.gss_wrapex(&public_key_data)?);
    link.write(&challenge)?;

    // now server respond normally with the original public key incremented by one
    let inc_pub_key = security_interface.gss_unwrapex(&(read_ts_validate(&(link.read(0)?))?))?;

    // Check possible man in the middle using cssp
    if BigUint::from_bytes_le(&inc_pub_key) != BigUint::from_bytes_le(&public_key_data) + BigUint::new(vec![1]) {
        return Err(Error::RdpError(RdpError::new(RdpErrorKind::PossibleMITM, "Man in the middle detected")))
    }

    // compute the last message with encoded credentials

    let domain = if restricted_admin_mode { vec![] } else { authentication_protocol.get_domain_name()};
    let user = if restricted_admin_mode { vec![] } else { authentication_protocol.get_user_name() };
    let password = if restricted_admin_mode { vec![] } else { authentication_protocol.get_password() };

    let credentials = create_ts_authinfo(security_interface.gss_wrapex(&create_ts_credentials(domain, user, password))?);
    link.write(&credentials)?;

    Ok(())
}

#[cfg(test)]
mod test {
    use super::*;

    #[test]
    fn test_create_ts_credentials() {
        let credentials = create_ts_credentials(b"domain".to_vec(), b"user".to_vec(), b"password".to_vec());
        let result =  [48, 41, 160, 3, 2, 1, 1, 161, 34, 4, 32, 48, 30, 160, 8, 4, 6, 100, 111, 109, 97, 105, 110, 161, 6, 4, 4, 117, 115, 101, 114, 162, 10, 4, 8, 112, 97, 115, 115, 119, 111, 114, 100];
        assert_eq!(credentials[0..32], result[0..32]);
        assert_eq!(credentials[33..43], result[33..43]);
    }

    #[test]
    fn test_create_ts_authinfo() {
        assert_eq!(create_ts_authinfo(b"foo".to_vec()), [48, 12, 160, 3, 2, 1, 2, 162, 5, 4, 3, 102, 111, 111])
    }
}