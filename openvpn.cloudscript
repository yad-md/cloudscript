cloudscript openvpn_multi_stack
    version             = _latest
    result_template     = openvpn_pair_result_tmpl

globals
    server_password	    = lib::random_password()
    console_password    = lib::random_password()
    openvpn_slice_user  = 'openvpn'

thread openvpn_setup
    tasks               = [openvpn_server_client_setup]
    
task openvpn_server_client_setup

    #-----------------------
    # Keys
    #-----------------------

    /key/password openvpn_server_pass_key read_or_create
        key_group       = _SERVER
        password        = server_password

    /key/password openvpn_server_console_key read_or_create
        key_group       = _CONSOLE
        password        = console_password        

    # create storage slice keys
    /key/token openvpn_slice_key read_or_create
        username        = openvpn_slice_user

    #-------------------------------
    # create openvpn bootstrap
    #-------------------------------
    
    # create slice to store script in cloudstorage
    /storage/slice openvpn_slice read_or_create
        keys            = [openvpn_slice_key]
    
    # create slice container to store script in cloudstorage
    /storage/container openvpn_container read_or_create
        slice           = openvpn_slice
    
    # place script data in cloudstorage
    /storage/object openvpn_server_script_object => [openvpn_slice] read_or_create
        container_name  = 'openvpn_container'
        file_name       = 'openvpn_server_script.sh'
        slice           = openvpn_slice
        content_data    = openvpn_server_script_tmpl
        
    # associate the cloudstorage object with the openvpn script
    /orchestration/script openvpn_server_script => [openvpn_slice, openvpn_container, openvpn_server_script_object] read_or_create
        data_uri        = 'cloudstorage://openvpn_slice/openvpn_container/openvpn_server_script.sh'
        script_type     = _shell
        encoding        = _storage

    # place script data in cloudstorage
    /storage/object openvpn_client_script_object => [openvpn_slice, openvpn_server] read_or_create
        container_name  = 'openvpn_container'
        file_name       = 'openvpn_client_script.sh'
        slice           = openvpn_slice
        content_data    = openvpn_client_script_tmpl
        b64decode       = 0
        
    # associate the cloudstorage object with the openvpn script
    /orchestration/script openvpn_client_script => [openvpn_slice, openvpn_container, openvpn_client_script_object] read_or_create
        data_uri        = 'cloudstorage://openvpn_slice/openvpn_container/openvpn_client_script.sh'
        script_type     = _shell
        encoding        = _storage
    
    #-------------------------------
    # create openvpn server recipe
    #-------------------------------
    
    /orchestration/recipe openvpn_server_recipe read_or_create
        scripts         = [openvpn_server_script]

    /orchestration/recipe openvpn_client_recipe read_or_create
        scripts         = [openvpn_client_script]

    #-----------------------
    # Cloud Servers
    #-----------------------

    /server/cloud openvpn_server read_or_create
        hostname        = 'openvpn-server'
        image           = 'Linux Ubuntu Server 10.04 LTS 64-bit'
        service_type    = 'CS1'
        keys            = [openvpn_server_pass_key, openvpn_server_console_key]
        recipes         = [openvpn_server_recipe]

    /server/cloud openvpn_client read_or_create
        hostname        = 'openvpn-client'
        image           = 'Linux Ubuntu Server 10.04 LTS 64-bit'
        service_type    = 'CS1'
        keys            = [openvpn_server_pass_key, openvpn_server_console_key]
        recipes         = [openvpn_client_recipe]

text_template openvpn_pair_result_tmpl

Thank you for provisioning a openvpn server/client setup.

You can login to the server server directly via SSH by connecting
to root@{{ openvpn_server.ipaddress_public }} using the password:

{{ openvpn_server_pass_key.password }}

You can login to the openvpn client directly via SSH by connecting
to root@{{ openvpn_client.ipaddress_public }} using the password:

{{ openvpn_server_pass_key.password }}

_eof

text_template openvpn_server_script_tmpl
#!/bin/bash

apt-get install -y openvpn gadmin-openvpn-server bridge-utils > /dev/null

# Generate a static key
cd /etc/openvpn
openvpn --genkey --secret static.key > /dev/null

# set a cloudscript variable to key contents so it may be retrieved by client during client setup.
SERVER_KEY="`cat static.key`"

# Server configuration file
echo '
dev tun
ifconfig 172.16.0.1 172.16.0.2
secret static.key
' > /etc/openvpn/server.conf

/etc/init.d/openvpn start > /dev/null

# only output/echo JSON so it can be processed by cloudscript <*>.results.server_key
echo "{ \"server_key\":\"$SERVER_KEY\" }"

_eof

text_template openvpn_client_script_tmpl
#!/bin/bash

apt-get install -y openvpn gadmin-openvpn-client

echo '
remote {{ openvpn_server.ipaddress_private }}
dev tun
ifconfig 172.16.0.2 172.16.0.1
secret static.key
' > /etc/openvpn/client.conf

# Retrieve static key from cloudscript variable 
echo  "{{ openvpn_server.results.server_key }}" >  /etc/openvpn/static.key
chmod 0600 /etc/openvpn/static.key

echo "Run '/usr/sbin/gadmin-openvpn-server' for GUI admin tool to configure and manage OpenVPN server."
echo "Run '/usr/sbin/gadmin-openvpn-client' for GUI admin tool to configure and manage OpenVPN client."

/etc/init.d/openvpn start

_eof

