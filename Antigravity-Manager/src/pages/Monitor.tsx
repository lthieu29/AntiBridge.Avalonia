import React from 'react';
import { ProxyMonitor } from '../components/proxy/ProxyMonitor';

const Monitor: React.FC = () => {
    return (
        <div className="h-full w-full overflow-hidden">
            <ProxyMonitor className="h-full" />
        </div>
    );
};

export default Monitor;