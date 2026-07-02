use model::error::{RdpResult, Error, RdpError, RdpErrorKind};
use std::io::{Cursor, Read, Write};
use model::data::Message;
use std::sync::Arc;
use std::convert::TryFrom;

/// Custom TLS stream wrapping a synchronous IO + rustls ClientConnection
pub struct TlsStream<S: Read + Write> {
    inner: S,
    conn: rustls::ClientConnection,
}

impl<S: Read + Write> Read for TlsStream<S> {
    fn read(&mut self, buf: &mut [u8]) -> std::io::Result<usize> {
        let mut stream = rustls::Stream::new(&mut self.conn, &mut self.inner);
        stream.read(buf)
    }
}

impl<S: Read + Write> Write for TlsStream<S> {
    fn write(&mut self, buf: &[u8]) -> std::io::Result<usize> {
        let mut stream = rustls::Stream::new(&mut self.conn, &mut self.inner);
        stream.write(buf)
    }
    fn flush(&mut self) -> std::io::Result<()> {
        let mut stream = rustls::Stream::new(&mut self.conn, &mut self.inner);
        stream.flush()
    }
}

/// This a wrapper to work equals
/// for a stream and a TLS stream
pub enum Stream<S: Read + Write> {
    /// Raw stream that implement Read + Write
    Raw(S),
    /// TLS Stream
    Ssl(TlsStream<S>)
}

impl<S: Read + Write> Stream<S> {
    pub fn read_exact(&mut self, buf: &mut[u8]) -> RdpResult<()> {
        match self {
            Stream::Raw(e) => e.read_exact(buf)?,
            Stream::Ssl(e) => e.read_exact(buf)?
        };
        Ok(())
    }

    pub fn read(&mut self, buf: &mut[u8]) -> RdpResult<usize> {
        match self {
            Stream::Raw(e) => Ok(e.read(buf)?),
            Stream::Ssl(e) => Ok(e.read(buf)?)
        }
    }

    pub fn write(&mut self, buffer: &[u8]) -> RdpResult<usize> {
        Ok(match self {
            Stream::Raw(e) => e.write(buffer)?,
            Stream::Ssl(e) => e.write(buffer)?
        })
    }

    pub fn shutdown(&mut self) -> RdpResult<()> {
        match self {
            Stream::Ssl(e) => {
                e.conn.send_close_notify();
                let _ = e.conn.complete_io(&mut e.inner);
                Ok(())
            },
            _ => Ok(())
        }
    }
}

/// Link layer is a wrapper around TCP or SSL stream
/// It can swicth from TCP to SSL
pub struct Link<S: Read + Write> {
    stream: Stream<S>
}

impl<S: Read + Write> Link<S> {
    pub fn new(stream: Stream<S>) -> Self {
        Link { stream }
    }

    pub fn write(&mut self, message: &dyn Message) -> RdpResult<()> {
        let mut buffer = Cursor::new(Vec::new());
        message.write(&mut buffer)?;
        self.stream.write(buffer.into_inner().as_slice())?;
        Ok(())
    }

    pub fn read(&mut self, expected_size: usize) -> RdpResult<Vec<u8>> {
        if expected_size == 0 {
            let mut buffer = vec![0; 1500];
            let size = self.stream.read(&mut buffer)?;
            buffer.resize(size, 0);
            Ok(buffer)
        }
        else {
            let mut buffer = vec![0; expected_size];
            self.stream.read_exact(&mut buffer)?;
            Ok(buffer)
        }
    }

    /// Start a ssl connection from a raw stream
    pub fn start_ssl(self, check_certificate: bool, hostname: &str) -> RdpResult<Link<S>> {
        let config = if check_certificate {
            let mut roots = rustls::RootCertStore::empty();
            roots.extend(webpki_roots::TLS_SERVER_ROOTS.iter().cloned());
            rustls::ClientConfig::builder()
                .with_root_certificates(roots)
                .with_no_client_auth()
        } else {
            rustls::ClientConfig::builder()
                .dangerous()
                .with_custom_certificate_verifier(Arc::new(NoCertVerifier))
                .with_no_client_auth()
        };

        // Leak hostname string to get a 'static lifetime for the ServerName
        let hostname_static: &'static str = Box::leak(hostname.to_string().into_boxed_str());
        let server_name = rustls::pki_types::ServerName::try_from(hostname_static)
            .map_err(|e| RdpError::new(RdpErrorKind::InvalidData, &format!("Invalid TLS hostname: {:?}", e)))?;

        let conn = rustls::ClientConnection::new(Arc::new(config), server_name)
            .map_err(|e| RdpError::new(RdpErrorKind::InvalidData, &format!("TLS connection creation failed: {:?}", e)))?;

        if let Stream::Raw(inner) = self.stream {
            let mut tls_stream = TlsStream { inner, conn };
            tls_stream.conn.complete_io(&mut tls_stream.inner)
                .map_err(|e| RdpError::new(RdpErrorKind::ProtocolNegFailure, &format!("TLS handshake failed: {:?}", e)))?;
            return Ok(Link::new(Stream::Ssl(tls_stream)))
        }
        Err(Error::RdpError(RdpError::new(RdpErrorKind::NotImplemented, "start_ssl on ssl stream is forbidden")))
    }

    /// Retrive the peer certificate in DER format
    pub fn get_peer_certificate(&self) -> RdpResult<Option<Vec<u8>>> {
        if let Stream::Ssl(stream) = &self.stream {
            Ok(stream.conn.peer_certificates()
                .and_then(|certs| certs.first())
                .map(|cert| cert.to_vec()))
        }
        else {
            Err(Error::RdpError(RdpError::new(RdpErrorKind::InvalidData, "get peer certificate on non ssl link is impossible")))
        }
    }

    pub fn shutdown(&mut self) -> RdpResult<()> {
        self.stream.shutdown()
    }

    #[cfg(feature = "integration")]
    pub fn get_stream(self) -> Stream<S> {
        self.stream
    }
}

#[derive(Debug)]
struct NoCertVerifier;

impl rustls::client::danger::ServerCertVerifier for NoCertVerifier {
    fn verify_server_cert(
        &self,
        _end_entity: &rustls::pki_types::CertificateDer<'_>,
        _intermediates: &[rustls::pki_types::CertificateDer<'_>],
        _server_name: &rustls::pki_types::ServerName<'_>,
        _ocsp_response: &[u8],
        _now: rustls::pki_types::UnixTime,
    ) -> Result<rustls::client::danger::ServerCertVerified, rustls::Error> {
        Ok(rustls::client::danger::ServerCertVerified::assertion())
    }

    fn verify_tls12_signature(
        &self,
        _message: &[u8],
        _cert: &rustls::pki_types::CertificateDer<'_>,
        _dss: &rustls::DigitallySignedStruct,
    ) -> Result<rustls::client::danger::HandshakeSignatureValid, rustls::Error> {
        Ok(rustls::client::danger::HandshakeSignatureValid::assertion())
    }

    fn verify_tls13_signature(
        &self,
        _message: &[u8],
        _cert: &rustls::pki_types::CertificateDer<'_>,
        _dss: &rustls::DigitallySignedStruct,
    ) -> Result<rustls::client::danger::HandshakeSignatureValid, rustls::Error> {
        Ok(rustls::client::danger::HandshakeSignatureValid::assertion())
    }

    fn supported_verify_schemes(&self) -> Vec<rustls::SignatureScheme> {
        vec![
            rustls::SignatureScheme::RSA_PKCS1_SHA1,
            rustls::SignatureScheme::RSA_PKCS1_SHA256,
            rustls::SignatureScheme::RSA_PKCS1_SHA384,
            rustls::SignatureScheme::RSA_PKCS1_SHA512,
            rustls::SignatureScheme::RSA_PSS_SHA256,
            rustls::SignatureScheme::RSA_PSS_SHA384,
            rustls::SignatureScheme::RSA_PSS_SHA512,
            rustls::SignatureScheme::ECDSA_NISTP256_SHA256,
            rustls::SignatureScheme::ECDSA_NISTP384_SHA384,
            rustls::SignatureScheme::ECDSA_NISTP521_SHA512,
            rustls::SignatureScheme::ED25519,
        ]
    }
}
