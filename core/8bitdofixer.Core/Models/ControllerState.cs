namespace BitDoFixer.Models;

// public, no internal: WPF bindea por reflexión y falla en silencio contra
// tipos internal. Todo lo que aparece en un Binding del XAML tiene que ser public.

/// <summary>Estado de un mando individual.</summary>
public enum ControllerState
{
    /// <summary>Detectado, todavía adquiriendo el dispositivo y conectando el pad virtual.</summary>
    Connecting,

    /// <summary>Mapeando: el loop de polling está corriendo.</summary>
    Mapped,

    /// <summary>El dispositivo físico desapareció. El pad virtual sigue vivo durante la ventana de gracia.</summary>
    Lost
}

/// <summary>
/// Estado agregado del servicio. DriverMissing vive acá y no en ControllerState porque
/// el ViGEmClient se crea una sola vez a nivel de servicio: sin cliente no hay ninguna
/// entry, así que un mando individual nunca puede estar en ese estado.
/// </summary>
public enum ServiceState
{
    Stopped,

    /// <summary>Corriendo, cero mandos encontrados. Es el estado de reposo normal, no un error.</summary>
    Searching,

    /// <summary>Uno o más mandos mapeados.</summary>
    Mapped,

    /// <summary>No se pudo crear el ViGEmClient: falta ViGEmBus.</summary>
    DriverMissing
}
