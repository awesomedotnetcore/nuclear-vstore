﻿using System;
using System.Collections.Generic;

using NuClear.VStore.Descriptors.Objects;

namespace NuClear.VStore.Objects.Validate
{
    public static class LinkValidator
    {
        public static IEnumerable<Exception> CorrectLink(IObjectElementDescriptor descriptor)
        {
            if (string.IsNullOrEmpty(descriptor.Value.Raw))
            {
                return Array.Empty<Exception>();
            }

            // We can use Uri.IsWellFormedUriString() instead:
            Uri uri;
            if (!Uri.TryCreate(descriptor.Value.Raw, UriKind.Absolute, out uri)
                || (uri.Scheme != "http" && uri.Scheme != "https")
                || uri.HostNameType != UriHostNameType.Dns)
            {
                return new[] { new IncorrectLinkException(descriptor.Id) };
            }

            return Array.Empty<Exception>();
        }
    }
}
