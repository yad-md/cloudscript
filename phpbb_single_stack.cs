cloudscript phpbb
    version              = '2012-05-20'
    result_template      = phpbb_result_template

globals
    server_password      = lib::random_password()
    console_password     = lib::random_password()
    phpbb_db_password    = lib::random_password()
    phpbb_db_name        = 'phpbb'
    phpbb_db_username    = 'phpbb'


thread core
    tasks                = [install]
    
task install
    /key/password phpbb_server_key read_or_create
        key_group        = _SERVER
        password         = server_password
    
    /key/password phpbb_console_key read_or_create
        key_group        = _CONSOLE
        password         = console_password
    #
    # create phpbb storage slice, bootstrap script and recipe
    #
    
    # storage slice key
    /key/token phpbb_slice_key read_or_create
        username         = 'phpbbsliceuser'

    # slice
    /storage/slice phpbb_slice read_or_create
        keys             = [phpbb_slice_key]

    # slice container
    /storage/container phpbb_container => [phpbb_slice] read_or_create
        slice            = phpbb_slice

    # store script as object in cloudstorage
    /storage/object install_phpbb_script_object => [phpbb_slice, phpbb_container] read_or_create
        container_name   = 'phpbb_container'
        file_name        = 'install_phpbb.sh'
        slice            =  phpbb_slice
        content_data     =  install_phpbb_sh

    # associate the cloudstorage object with the phpbb script
    /orchestration/script install_phpbb_script => [phpbb_slice, phpbb_container, install_phpbb_script_object] read_or_create
        data_uri         = 'cloudstorage://phpbb_slice/phpbb_container/install_phpbb.sh'
        script_type      = _SHELL
        encoding         = _STORAGE

    # create the recipe and associate the script
    /orchestration/recipe install_phpbb_recipe read_or_create
        scripts          = [install_phpbb_script]

    # phpbb node
    /server/cloud phpbb_server read_or_create
        hostname         = 'phpbb'
        image            = 'Linux Ubuntu Server 10.04 LTS 64-bit'
        service_type     = 'CS05'
        keys             = [phpbb_server_key, phpbb_console_key]
        recipes          = [install_phpbb_recipe]


text_template phpbb_result_template

Thank you for provisioning a phpbb service.

You can now finish its configuration on the following page:

http://{{ phpbb_server.ipaddress_public }}/phpbb/

Please use following credentials for database configuration:

hostname: localhost
username: {{ phpbb_db_username }}
password: {{ phpbb_db_password }}
database: {{ phpbb_db_name }}


You can also login to the server directly via SSH by connecting
to root@{{ phpbb_server.ipaddress_public }} using the password:

{{ phpbb_server_key.password }}

_eof

text_template install_phpbb_sh
#!/bin/bash
#
# install phpbb service
#

# check permissions
[ `whoami` = 'root' ] || {
    echo "ERROR: must have root permissions to execute the commands"
    exit 1
}

apt-get update > /dev/null
[ $? -eq 0 ] && echo "OK: update local apt cache" || {
    echo "ERROR: update local apt cache"
    exit 1
}

# install unzip
apt-get install -y unzip > /dev/null
[ $? -eq 0 ] && echo "OK: install unzip" || {
    echo "ERROR: install unzip"
    exit 1
}

# install MySQL
DEBIAN_FRONTEND=noninteractive apt-get install -y mysql-server mysql-client > /dev/null
[ $? -eq 0 ] && echo "OK: install MySQL" || {
    echo "ERROR: install MySQL"
    exit 1
}

# install Apache2
DEBIAN_FRONTEND=noninteractive apt-get install -y apache2 > /dev/null
[ $? -eq 0 ] && echo "OK: install Apache2" || {
    echo "ERROR: install Apache2"
    exit 1
}

# install PHP5
apt-get install -y php5 php5-cli libapache2-mod-php5 > /dev/null
[ $? -eq 0 ] && echo "OK: install PHP5" || {
    echo "ERROR: install PHP5"
    exit 1
}
# install required PHP5 modules
apt-get install -y php5-mysql php5-memcache php5-curl php5-gd php5-imap > /dev/null
[ $? -eq 0 ] && echo "OK: install PHP5 modules" || {
    echo "ERROR: install PHP5 modules"
    exit 1
}
# enable PHP5 module
a2enmod php5 > /dev/null

# download phpbb source
cd /var/www && wget -O phpbb.zip "https://www.phpbb.com/files/release/phpBB-3.0.11.zip" > /dev/null 2>&1
[ $? -eq 0 ] && echo "OK: download phpbb source" || {
    echo "ERROR: download phpbb source"
    exit 1
}
# extract phpbb files
unzip phpbb.zip > /dev/null
[ $? -eq 0 ] && echo "OK: extract phpbb files" || {
    echo "ERROR: extract phpbb files"
    exit 1
}
# move phpbb files
cd /var/www/ && mv ./phpBB3 ./phpbb
[ $? -eq 0 ] && echo "OK: move phpbb files" || {
    echo "ERROR: move phpbb files"
    exit 1
}
# set ownership & permissions
chown -R www-data:www-data /var/www/phpbb
[ $? -eq 0 ] && echo "OK: set required permissions" || {
    echo "ERROR: set required permissions"
    exit 1
}

# create MySQL database & user
mysql <<EOF
create database {{ phpbb_db_name }};
grant all on {{ phpbb_db_name }}.* to {{ phpbb_db_username }}@localhost identified by '{{ phpbb_db_password }}';
EOF
[ $? -eq 0 ] && echo "OK: create phpbb database and user" || {
    echo "ERROR: create phpbb database and user"
    exit 1
}

# enable mod_rewrite
a2enmod rewrite > /dev/null 2>&1

# restart web server
/etc/init.d/apache2 stop  > /dev/null 2>&1
/etc/init.d/apache2 start > /dev/null 2>&1

echo "OK: install phpbb service"

_eof

